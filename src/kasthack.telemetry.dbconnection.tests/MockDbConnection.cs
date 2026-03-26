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
    public List<string> ExecutedCommandTexts { get; } = [];

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
        Thread.Sleep(CommandDelay);

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        new MockDbTransaction(this, isolationLevel);

    protected override DbCommand CreateDbCommand() =>
        new MockDbCommand(this);

    protected override DbBatch CreateDbBatch() => new MockDbBatch(this);

    public override DataTable GetSchema() => new();
    public override DataTable GetSchema(string collectionName) => new();
    public override DataTable GetSchema(string collectionName, string?[] restrictionValues) => new();
}

internal sealed class MockDbCommand(MockDbConnection? connection) : DbCommand
{
#pragma warning disable CA2213 // Mock object
    private MockDbConnection? _connection = connection;
#pragma warning restore CA2213 //
    private readonly MockDbParameterCollection _parameters = [];

    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; } = 30;
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection
    {
        get => _connection;
        set => _connection = value as MockDbConnection;
    }

    protected override DbParameterCollection DbParameterCollection => _parameters;

    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() =>
        Thread.Sleep(_connection!.CommandDelay);

    public override void Prepare() =>
        Thread.Sleep(_connection!.CommandDelay);

    public override Task PrepareAsync(CancellationToken cancellationToken = default) =>
        Task.Delay(_connection!.CommandDelay, cancellationToken);

    public override int ExecuteNonQuery()
    {
        Thread.Sleep(_connection!.CommandDelay);
        _connection.ExecutedCommandTexts.Add(CommandText);
        return 0;
    }

    public override object? ExecuteScalar()
    {
        Thread.Sleep(_connection!.CommandDelay);
        _connection.ExecutedCommandTexts.Add(CommandText);
        return null;
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        Thread.Sleep(_connection!.CommandDelay);
        _connection.ExecutedCommandTexts.Add(CommandText);
        return new MockDbDataReader();
    }

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(_connection!.CommandDelay, cancellationToken).ConfigureAwait(false);
        _connection.ExecutedCommandTexts.Add(CommandText);
        return 0;
    }

    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(_connection!.CommandDelay, cancellationToken).ConfigureAwait(false);
        _connection.ExecutedCommandTexts.Add(CommandText);
        return null;
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        await Task.Delay(_connection!.CommandDelay, cancellationToken).ConfigureAwait(false);
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

internal sealed class MockDbTransaction(MockDbConnection connection, IsolationLevel isolationLevel) : DbTransaction
{
#pragma warning disable CA2213 // Mock object
    private readonly MockDbConnection _connection = connection;
#pragma warning restore CA2213 //

    public override IsolationLevel IsolationLevel { get; } = isolationLevel;
    protected override DbConnection DbConnection => _connection;

    public override void Commit() =>
        Thread.Sleep(_connection.CommandDelay);

    public override Task CommitAsync(CancellationToken cancellationToken = default) =>
        Task.Delay(_connection.CommandDelay, cancellationToken);

    public override void Rollback() =>
        Thread.Sleep(_connection.CommandDelay);

    public override Task RollbackAsync(CancellationToken cancellationToken = default) =>
        Task.Delay(_connection.CommandDelay, cancellationToken);

    public override bool SupportsSavepoints => true;

    public override void Save(string savepointName) => Thread.Sleep(_connection.CommandDelay);

    public override Task SaveAsync(string savepointName, CancellationToken cancellationToken = default) =>
        Task.Delay(_connection.CommandDelay, cancellationToken);

    public override void Rollback(string savepointName) => Thread.Sleep(_connection.CommandDelay);

    public override Task RollbackAsync(string savepointName, CancellationToken cancellationToken = default) =>
        Task.Delay(_connection.CommandDelay, cancellationToken);

    public override void Release(string savepointName) => Thread.Sleep(_connection.CommandDelay);

    public override Task ReleaseAsync(string savepointName, CancellationToken cancellationToken = default) =>
        Task.Delay(_connection.CommandDelay, cancellationToken);
}

internal sealed class MockDbParameterCollection : DbParameterCollection{
    private readonly List<DbParameter> _items = [];

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
        foreach (var item in values)
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

internal sealed class MockDbBatch(MockDbConnection connection) : DbBatch
{
#pragma warning disable CA2213 // Mock object
    private readonly MockDbConnection _connection = connection;
#pragma warning restore CA2213 // 

    public override int Timeout { get; set; } = 30;
    protected override DbBatchCommandCollection DbBatchCommands => new MockDbBatchCommandCollection();
    protected override DbConnection? DbConnection { get => _connection; set { } }
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() => Thread.Sleep(_connection.CommandDelay);

    public override int ExecuteNonQuery()
    {
        Thread.Sleep(_connection.CommandDelay);
        return 0;
    }

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(_connection.CommandDelay, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    public override object? ExecuteScalar()
    {
        Thread.Sleep(_connection.CommandDelay);
        return null;
    }

    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(_connection.CommandDelay, cancellationToken).ConfigureAwait(false);
        return null;
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        Thread.Sleep(_connection.CommandDelay);
        return new MockDbDataReader();
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        await Task.Delay(_connection.CommandDelay, cancellationToken).ConfigureAwait(false);
        return new MockDbDataReader();
    }

    public override void Prepare() => Thread.Sleep(_connection.CommandDelay);
    public override Task PrepareAsync(CancellationToken cancellationToken = default) => Task.Delay(_connection.CommandDelay, cancellationToken);

    protected override DbBatchCommand CreateDbBatchCommand() => new MockDbBatchCommand();
}

internal sealed class MockDbBatchCommand : DbBatchCommand
{
    public override string CommandText { get; set; } = string.Empty;
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override int RecordsAffected => 0;
    protected override DbParameterCollection DbParameterCollection => new MockDbParameterCollection();
}

internal sealed class MockDbBatchCommandCollection : DbBatchCommandCollection
{
    private readonly List<DbBatchCommand> _items = [];
    public override int Count => _items.Count;
    public override bool IsReadOnly => false;
    public override void Add(DbBatchCommand item) => _items.Add(item);
    public override int IndexOf(DbBatchCommand item) => _items.IndexOf(item);
    public override void Insert(int index, DbBatchCommand item) => _items.Insert(index, item);
    public override void RemoveAt(int index) => _items.RemoveAt(index);
    public override void Clear() => _items.Clear();
    public override bool Contains(DbBatchCommand item) => _items.Contains(item);
    public override void CopyTo(DbBatchCommand[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public override bool Remove(DbBatchCommand item) => _items.Remove(item);
    public override IEnumerator<DbBatchCommand> GetEnumerator() => _items.GetEnumerator();
    protected override DbBatchCommand GetBatchCommand(int index) => _items[index];
    protected override void SetBatchCommand(int index, DbBatchCommand batchCommand) => _items[index] = batchCommand;
}
