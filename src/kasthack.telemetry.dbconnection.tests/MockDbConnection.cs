using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace kasthack.telemetry.dbconnection.tests;

internal sealed class MockDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;

    public TimeSpan CommandDelay { get; set; } = TimeSpan.Zero;
    public int OpenCalled { get; private set; }
    public int CloseCalled { get; private set; }
    public List<string> ExecutedCommandTexts { get; } = new();

    [AllowNull]
    public override string ConnectionString
    {
        get => string.Empty;
        set { }
    }

    public override string Database => "MockDb";
    public override string DataSource => string.Empty;
    public override string ServerVersion => "0";
    public override ConnectionState State => _state;

    public override void Open()
    {
        Thread.Sleep(CommandDelay);
        _state = ConnectionState.Open;
        OpenCalled++;
    }

    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(CommandDelay, cancellationToken).ConfigureAwait(false);
        _state = ConnectionState.Open;
        OpenCalled++;
    }

    public override void Close()
    {
        _state = ConnectionState.Closed;
        CloseCalled++;
    }

    public override void ChangeDatabase(string databaseName) =>
        throw new NotSupportedException();

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        new MockDbTransaction(this, isolationLevel);

    protected override DbCommand CreateDbCommand() =>
        new MockDbCommand(this);
}

internal sealed class MockDbCommand : DbCommand
{
    private readonly MockDbConnection _connection;
    private DbConnection? _dbConnection;
    private DbTransaction? _dbTransaction;
    private readonly MockDbParameterCollection _parameters;

    public MockDbCommand(MockDbConnection connection)
    {
        _connection = connection;
        _dbConnection = connection;
        _parameters = new MockDbParameterCollection();
    }

    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; } = 30;
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection
    {
        get => _dbConnection;
        set => _dbConnection = value;
    }

    protected override DbParameterCollection DbParameterCollection => _parameters;

    protected override DbTransaction? DbTransaction
    {
        get => _dbTransaction;
        set => _dbTransaction = value;
    }

    public override void Cancel() =>
        Thread.Sleep(_connection.CommandDelay);

    public override void Prepare() =>
        Thread.Sleep(_connection.CommandDelay);

    public override Task PrepareAsync(CancellationToken cancellationToken = default) =>
        Task.Delay(_connection.CommandDelay, cancellationToken);

    public override int ExecuteNonQuery()
    {
        Thread.Sleep(_connection.CommandDelay);
        _connection.ExecutedCommandTexts.Add(CommandText);
        return 0;
    }

    public override object? ExecuteScalar()
    {
        Thread.Sleep(_connection.CommandDelay);
        _connection.ExecutedCommandTexts.Add(CommandText);
        return null;
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        Thread.Sleep(_connection.CommandDelay);
        _connection.ExecutedCommandTexts.Add(CommandText);
        return new MockDbDataReader();
    }

    protected override DbParameter CreateDbParameter() =>
        throw new NotSupportedException();
}

internal sealed class MockDbDataReader : DbDataReader
{
    public override int FieldCount => 0;
    public override bool HasRows => false;
    public override bool IsClosed => false;
    public override int RecordsAffected => 0;
    public override int Depth => 0;
    public override object this[int ordinal] => throw new NotSupportedException();
    public override object this[string name] => throw new NotSupportedException();
    public override bool Read() => false;
    public override bool NextResult() => false;
    public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
    public override byte GetByte(int ordinal) => throw new NotSupportedException();
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override char GetChar(int ordinal) => throw new NotSupportedException();
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override string GetDataTypeName(int ordinal) => throw new NotSupportedException();
    public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
    public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
    public override double GetDouble(int ordinal) => throw new NotSupportedException();
    public override Type GetFieldType(int ordinal) => throw new NotSupportedException();
    public override float GetFloat(int ordinal) => throw new NotSupportedException();
    public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
    public override short GetInt16(int ordinal) => throw new NotSupportedException();
    public override int GetInt32(int ordinal) => throw new NotSupportedException();
    public override long GetInt64(int ordinal) => throw new NotSupportedException();
    public override string GetName(int ordinal) => throw new NotSupportedException();
    public override int GetOrdinal(string name) => throw new NotSupportedException();
    public override string GetString(int ordinal) => throw new NotSupportedException();
    public override object GetValue(int ordinal) => throw new NotSupportedException();
    public override int GetValues(object[] values) => throw new NotSupportedException();
    public override bool IsDBNull(int ordinal) => throw new NotSupportedException();
    public override IEnumerator GetEnumerator() => throw new NotSupportedException();
}

internal sealed class MockDbTransaction : DbTransaction
{
    private readonly MockDbConnection _connection;

    public MockDbTransaction(MockDbConnection connection, IsolationLevel isolationLevel)
    {
        _connection = connection;
        IsolationLevel = isolationLevel;
    }

    public override IsolationLevel IsolationLevel { get; }
    protected override DbConnection DbConnection => _connection;

    public override void Commit() =>
        Thread.Sleep(_connection.CommandDelay);

    public override Task CommitAsync(CancellationToken cancellationToken = default) =>
        Task.Delay(_connection.CommandDelay, cancellationToken);

    public override void Rollback() =>
        Thread.Sleep(_connection.CommandDelay);

    public override Task RollbackAsync(CancellationToken cancellationToken = default) =>
        Task.Delay(_connection.CommandDelay, cancellationToken);
}

internal sealed class MockDbParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _items = new();

    public override int Count => _items.Count;
    public override object SyncRoot => this;
    public override bool IsFixedSize => false;
    public override bool IsReadOnly => false;
    public override bool IsSynchronized => false;

    public override int Add(object value)
    {
        _items.Add((DbParameter)value);
        return _items.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (object? item in values)
        {
            if (item is DbParameter param)
            {
                _items.Add(param);
            }
        }
    }

    public override void Clear() => _items.Clear();

    public override bool Contains(string value)
    {
        foreach (var p in _items)
        {
            if (p.ParameterName == value)
            {
                return true;
            }
        }

        return false;
    }

    public override bool Contains(object value) => _items.Contains((DbParameter)value);

    public override void CopyTo(Array array, int index) =>
        ((ICollection)_items).CopyTo(array, index);

    public override IEnumerator GetEnumerator() => _items.GetEnumerator();

    public override int IndexOf(string parameterName)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].ParameterName == parameterName)
            {
                return i;
            }
        }

        return -1;
    }

    public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);

    public override void Insert(int index, object value) =>
        _items.Insert(index, (DbParameter)value);

    public override void Remove(object value) =>
        _items.Remove((DbParameter)value);

    public override void RemoveAt(int index) => _items.RemoveAt(index);

    public override void RemoveAt(string parameterName)
    {
        var idx = IndexOf(parameterName);
        if (idx >= 0)
        {
            _items.RemoveAt(idx);
        }
    }

    protected override DbParameter GetParameter(int index) => _items[index];

    protected override DbParameter GetParameter(string parameterName) =>
        _items[IndexOf(parameterName)];

    protected override void SetParameter(int index, DbParameter value) =>
        _items[index] = value;

    protected override void SetParameter(string parameterName, DbParameter value) =>
        _items[IndexOf(parameterName)] = value;
}
