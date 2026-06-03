// ***************************************************************************************************************************
// File: DatabaseConfig.cs
// Description: Single-file database configuration for FishLens. Edit the five constants below,
//              rebuild, and the app will target the new SQL Server instance automatically.
//              On first launch the bootstrap will create all tables, procedures, and seed data.
// Notes: DatabaseConfig.Schema is used as both the SQL schema name and the stored-procedure prefix.
// ***************************************************************************************************************************

namespace FishLens_App
{
    public static class DatabaseConfig
    {
        // ── Edit these five values before handing off to a new environment ─────────
        public const string Server   = "aura.cset.oit.edu,5433";
        public const string Database = "kaharra";
        public const string Username = "kaharra";
        public const string Password = "kaharra";

        /// <summary>
        /// SQL schema that owns all FishLens tables and stored procedures.
        /// Must match an existing (or creatable) schema in the target database.
        /// </summary>
        public const string Schema   = "kaharra";
        // ──────────────────────────────────────────────────────────────────────────

        public static string ConnectionString =>
            $"server={Server}; database={Database}; UID={Username}; password={Password}";
    }
}
