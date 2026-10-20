// =============================================================================
// GPAL - Generally Positive Automation Library
// Copyright © 2026 Software Decisions, Inc. All rights reserved.
//
// This file is part of GPAL.
// Licensed under the Business Source License 1.1
//
// Primary development, architecture, and vision by Michael B. Vederman,
// CEO of Software Decisions, Inc., Texas.
//
// Internal development maintained privately.
// Public releases appear on GitHub: https://github.com/SoftwareDecisionsInc/GPAL.
//
// See LICENSE for full terms, including Additional Use Grant.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    internal class DatabaseSettings
    {
        public GPALDatabase Database { get; set; }
        public DatabaseType DatabaseType { get; set; }
        public string DatabaseName { get; set; }
        public string TableName { get; set; }
        public string ServerName { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public SqlConnection Connection { get; set; }
        public SqlTransaction SqlTransaction { get; set; }
        public string ConnectionString { get; set; }
        private string ExecutionString { get; set; } = null;
        public string CreateSQL { get; set; } = null;
        public string ReadSQL { get; set; } = null;
        public string UpdateSQL { get; set; } = null;
        public string DeleteSQL { get; set; } = null;
        public System.Data.CommandType SqlCommandType { get; set; } = CommandType.Text;
        public List<DatabaseColumn> ColumnList { get; set; }
        public List<string> ParameterNamesList { get; set; }
        public List<string> ParametersList { get; set; }
        public IGPALGrid<string> ParametersGrid { get; set; }
        public GPALDatabase.DatabaseError LastError { get; set; }   // null until something fails
        public IGPALGrid<string> Tokens { get; set; }   // rows and columns of data to enter into text fields, CAVEAT: possibly something other than text? an image?
        public List<int> LastGridRowCountsPerTable { get; set; }   // set by SaveTo(out IGPALGrid<string>); read back by the optional GetRowCount() chained call
        public int RowCount { get; set; } = -1;
        public int CommandTimeoutSeconds { get; set; } = -1;   // -1 leaves the provider's own default alone
        public GPALDatabase.CallOnError CallOnErrorHandler { get; set; } = null;
        public string StoredProcedure
        {
            get
            {
                return ExecutionString;
            }
            internal set
            {
                SqlCommandType = CommandType.StoredProcedure;
                ExecutionString = value;
            }
        }
        public string SqlCommand
        {
            get
            {
                return ExecutionString;
            }
            set
            {
                SqlCommandType = CommandType.Text;
                ExecutionString = value;
            }
        }

        /// <summary>
        /// The chain's verb: Create, Read, Update or Delete.
        /// </summary>
        internal enum CrudVerb { None, Create, Read, Update, Delete }

        // The chain's verb. Its sql is chosen when the command is built and is not written into SqlCommand, so it does
        // not carry over to the next call or replace a stored procedure the caller set.
        internal CrudVerb Verb { get; set; } = CrudVerb.None;

        /// <summary>
        /// The sql the verb on the chain was given, null when that verb was given none of its own.
        /// </summary>
        internal string VerbSQL
        {
            get
            {
                string retVal = CrudVerb.Create == Verb ? CreateSQL
                              : CrudVerb.Read == Verb ? ReadSQL
                              : CrudVerb.Update == Verb ? UpdateSQL
                              : CrudVerb.Delete == Verb ? DeleteSQL
                              : null;

                return retVal;
            }
        }
        internal class DatabaseColumn
        {
            public string ColumnName { get; set; } = null;
            public SqlDbType ColumnType { get; set; }
            public int ColumnLength { get; set; }
            public int ColumnPrecision { get; set; }
            public int ColumnScale { get; set; }
            public string TvTableTypeName { get; set; } // UDTT name for SqlDbType.Structured
            public DatabaseColumn(string name)
            {
                ColumnName = name;
            }
            public DatabaseColumn(string name, SqlDbType sqlDbType)
            {
                ColumnName = name;
                ColumnType = sqlDbType;
            }
        }
        public DatabaseSettings(GPALDatabase database)
        {
            ColumnList = new List<DatabaseColumn>();
            ParameterNamesList = new List<string>();
            ParametersList = new List<string>();
            ParametersGrid = GPAL.Grid.ToGPALObject();
            Tokens = GPAL.Grid.ToGPALObject();
            Database = database;
        }

    }
}

