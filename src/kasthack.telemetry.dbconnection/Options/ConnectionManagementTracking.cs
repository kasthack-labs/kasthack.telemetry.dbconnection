namespace kasthack.telemetry.dbconnection.Options;

/// <summary>Controls which connection management operations are instrumented with traces and metrics.</summary>
[Flags]
public enum ConnectionManagementTracking
{
    /// <summary>No connection management operations are instrumented.</summary>
    None = 0,
    /// <summary>Instrument open operations.</summary>
    Open = 1,
    /// <summary>Instrument close operations.</summary>
    Close = 1 << 1,
    /// <summary>Instrument all connection management operations.</summary>
    All = Open | Close,
}
