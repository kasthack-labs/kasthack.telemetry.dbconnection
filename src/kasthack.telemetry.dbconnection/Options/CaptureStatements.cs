namespace kasthack.telemetry.dbconnection.Options;

/// <summary>
/// Controls which command types have their <c>db.statement</c> tag captured in traces and metrics.
/// </summary>
[Flags]
public enum CaptureStatements
{
    /// <summary>No statements are captured.</summary>
    None = 0,

    /// <summary>Statements from <see cref="System.Data.CommandType.StoredProcedure"/> commands are captured.</summary>
    StoredProcedures = 1,

    /// <summary>Statements from <see cref="System.Data.CommandType.Text"/> and other non-stored-procedure commands are captured.</summary>
    Text = 1 << 1,

    /// <summary>All statements are captured. Equivalent to <see cref="StoredProcedures"/> | <see cref="Text"/>.</summary>
    All = StoredProcedures | Text,
}
