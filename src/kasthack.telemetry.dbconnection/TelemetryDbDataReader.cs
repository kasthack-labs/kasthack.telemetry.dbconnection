using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;

#pragma warning disable CA1010 // DbDataReader itself does not implement IEnumerable<T>

namespace kasthack.telemetry.dbconnection;

/// <summary>
/// A <see cref="DbDataReader"/> decorator that measures the time between the <c>ExecuteReader</c> call
/// and the disposal of this reader, emitting the measurement via the owning <see cref="TelemetryDbConnection"/>.
/// </summary>
public sealed class TelemetryDbDataReader : DbDataReader
{
    private readonly DbDataReader _inner;
    private readonly TelemetryDbConnection _connection;
    private readonly Activity? _activity;
    private readonly long _startTimestamp;
    private readonly string _operation;
    private readonly string? _dbStatement;
    private bool _disposed;

    internal TelemetryDbDataReader(
        DbDataReader reader,
        TelemetryDbConnection connection,
        Activity? activity,
        long startTimestamp,
        string operation,
        string? dbStatement)
    {
        _inner = reader;
        _connection = connection;
        _activity = activity;
        _startTimestamp = startTimestamp;
        _operation = operation;
        _dbStatement = dbStatement;
    }

    // ── DbDataReader abstract properties ────────────────────────────────────

    /// <inheritdoc/>
    public override int Depth => _inner.Depth;

    /// <inheritdoc/>
    public override int FieldCount => _inner.FieldCount;

    /// <inheritdoc/>
    public override bool HasRows => _inner.HasRows;

    /// <inheritdoc/>
    public override bool IsClosed => _inner.IsClosed;

    /// <inheritdoc/>
    public override int RecordsAffected => _inner.RecordsAffected;

    /// <inheritdoc/>
    public override object this[int ordinal] => _inner[ordinal];

    /// <inheritdoc/>
    public override object this[string name] => _inner[name];

    // ── Navigation ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override bool Read() => _inner.Read();

    /// <inheritdoc/>
    public override Task<bool> ReadAsync(CancellationToken cancellationToken) =>
        _inner.ReadAsync(cancellationToken);

    /// <inheritdoc/>
    public override bool NextResult() => _inner.NextResult();

    /// <inheritdoc/>
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken) =>
        _inner.NextResultAsync(cancellationToken);

    /// <inheritdoc/>
    public override void Close() => _inner.Close();

    // ── Schema ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override DataTable? GetSchemaTable() => _inner.GetSchemaTable();

    // ── Typed accessors ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override bool GetBoolean(int ordinal) => _inner.GetBoolean(ordinal);

    /// <inheritdoc/>
    public override byte GetByte(int ordinal) => _inner.GetByte(ordinal);

    /// <inheritdoc/>
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
        _inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);

    /// <inheritdoc/>
    public override char GetChar(int ordinal) => _inner.GetChar(ordinal);

    /// <inheritdoc/>
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
        _inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);

    /// <inheritdoc/>
    public override string GetDataTypeName(int ordinal) => _inner.GetDataTypeName(ordinal);

    /// <inheritdoc/>
    public override DateTime GetDateTime(int ordinal) => _inner.GetDateTime(ordinal);

    /// <inheritdoc/>
    public override decimal GetDecimal(int ordinal) => _inner.GetDecimal(ordinal);

    /// <inheritdoc/>
    public override double GetDouble(int ordinal) => _inner.GetDouble(ordinal);

    /// <inheritdoc/>
    public override Type GetFieldType(int ordinal) => _inner.GetFieldType(ordinal);

    /// <inheritdoc/>
    public override float GetFloat(int ordinal) => _inner.GetFloat(ordinal);

    /// <inheritdoc/>
    public override Guid GetGuid(int ordinal) => _inner.GetGuid(ordinal);

    /// <inheritdoc/>
    public override short GetInt16(int ordinal) => _inner.GetInt16(ordinal);

    /// <inheritdoc/>
    public override int GetInt32(int ordinal) => _inner.GetInt32(ordinal);

    /// <inheritdoc/>
    public override long GetInt64(int ordinal) => _inner.GetInt64(ordinal);

    /// <inheritdoc/>
    public override string GetName(int ordinal) => _inner.GetName(ordinal);

    /// <inheritdoc/>
    public override int GetOrdinal(string name) => _inner.GetOrdinal(name);

    /// <inheritdoc/>
    public override string GetString(int ordinal) => _inner.GetString(ordinal);

    /// <inheritdoc/>
    public override object GetValue(int ordinal) => _inner.GetValue(ordinal);

    /// <inheritdoc/>
    public override int GetValues(object[] values) => _inner.GetValues(values);

    /// <inheritdoc/>
    public override bool IsDBNull(int ordinal) => _inner.IsDBNull(ordinal);

    // ── Enumeration ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override IEnumerator GetEnumerator() => _inner.GetEnumerator();

    // ── Disposal – stops the timing measurement ──────────────────────────────

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _connection.RecordDuration(_startTimestamp, _operation, _dbStatement, hadError: false);
            _activity?.Dispose();
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _connection.RecordDuration(_startTimestamp, _operation, _dbStatement, hadError: false);
            _activity?.Dispose();
            await _inner.DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }
}
