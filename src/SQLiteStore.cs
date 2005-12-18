using System;
using System.Collections;
using System.Text;

using System.Data;
using Mono.Data.SqliteClient;

namespace SemWeb.Stores {
	
	public class SqliteStore : SQLStore {
		IDbConnection dbcon;
		
		bool debug = false;
		
		public SqliteStore(string connectionString, string table)
			: base(table) {
			dbcon = new SqliteConnection(connectionString);
			dbcon.Open();
		}
		
		protected override bool SupportsNoDuplicates { get { return false; } }
		protected override bool SupportsInsertIgnore { get { return false; } }
		protected override bool SupportsInsertCombined { get { return false; } }
		protected override bool SupportsUseIndex { get { return false; } }
		protected override bool SupportsFastJoin { get { return false; } }
		
		protected override string CreateNullTest(string column) {
			return column + " ISNULL";
		}
		
		protected override void EscapedAppend(StringBuilder b, string str, bool quotes) {
			if (quotes) b.Append('\'');
			for (int i = 0; i < str.Length; i++) {
				char c = str[i];
				switch (c) {
					case '\'':
						b.Append(c);
						b.Append(c);
						break;
					default:
						b.Append(c);
						break;
				}
			}
			if (quotes) b.Append('\'');
		}
		
		public override void Close() {
			dbcon.Close();
		}
		
		protected override void RunCommand(string sql) {
			if (debug) Console.Error.WriteLine(sql);
			IDbCommand dbcmd = dbcon.CreateCommand();
			dbcmd.CommandText = sql;
			dbcmd.ExecuteNonQuery();
			dbcmd.Dispose();
		}
		
		protected override object RunScalar(string sql) {
			if (debug) Console.Error.Write(sql);
			IDbCommand dbcmd = dbcon.CreateCommand();
			dbcmd.CommandText = sql;
			object ret = dbcmd.ExecuteScalar();
			dbcmd.Dispose();
			if (debug) Console.Error.WriteLine(" => " + ret);
			return ret;
		}

		protected override IDataReader RunReader(string sql) {
			if (debug) Console.Error.WriteLine(sql);
			IDbCommand dbcmd = dbcon.CreateCommand();
			dbcmd.CommandText = sql;
			IDataReader reader = dbcmd.ExecuteReader();
			dbcmd.Dispose();
			return reader;
		}

		protected override void BeginTransaction() {
			RunCommand("BEGIN");
		}
		
		protected override void EndTransaction() {
			RunCommand("END");
		}
		
		protected override void CreateIndexes() {
			foreach (string cmd in GetCreateIndexCommands(TableName)) {
				try {
					RunCommand(cmd);
				} catch (Exception e) {
					if (debug) Console.Error.WriteLine(e);
				}
			}
		}
		static string[] GetCreateIndexCommands(string table) {
			return new string[] {
				"CREATE INDEX subject_index ON " + table + "_statements(subject,predicate,object,meta);",
				"CREATE INDEX predicate_index ON " + table + "_statements(predicate);",
				"CREATE INDEX object_index ON " + table + "_statements(object);",
				"CREATE INDEX meta_index ON " + table + "_statements(meta);",
			
				"CREATE INDEX literal_index ON " + table + "_literals(value);",
				"CREATE UNIQUE INDEX entity_index ON " + table + "_entities(value);"
				};
		}
	}
}