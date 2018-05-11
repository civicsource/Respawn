
namespace Respawn
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
#if !NETSTANDARD1_2
    using System.Data.SqlClient;
#endif
    using System.Linq;
    using System.Threading.Tasks;

    public class Checkpoint
    {
		private string[] _tablesToDelete;
		private Constraint[] _foreignKeysToDisable;
        private string _deleteSql;

        public string[] TablesToIgnore { get; set; } = new string[0];
        public string[] SchemasToInclude { get; set; } = new string[0];
        public string[] SchemasToExclude { get; set; } = new string[0];
        public IDbAdapter DbAdapter { get; set; } = Respawn.DbAdapter.SqlServer;

        public int? CommandTimeout { get; set; }

		private class Relationship
		{
            public string PrimaryKeyTable { get; set; }
            public string ForeignKeyTable { get; set; }
			public Constraint Constraint { get; set; }

            public bool IsSelfReferencing => PrimaryKeyTable == ForeignKeyTable;

        }

#if !NETSTANDARD1_2
        public virtual async Task Reset(string nameOrConnectionString)
        {
            using (var connection = new SqlConnection(nameOrConnectionString))
            {
                await connection.OpenAsync();

                await Reset(connection);
            }
        }
#endif

        public virtual async Task Reset(DbConnection connection)
        {
            if (string.IsNullOrWhiteSpace(_deleteSql))
            {
                await BuildDeleteTables(connection);
            }

            await ExecuteDeleteSqlAsync(connection);
        }

        private async Task ExecuteDeleteSqlAsync(DbConnection connection)
        {
            using (var tx = connection.BeginTransaction())
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandTimeout = CommandTimeout ?? cmd.CommandTimeout;
                cmd.CommandText = _deleteSql;
                cmd.Transaction = tx;

                await cmd.ExecuteNonQueryAsync();

                tx.Commit();
            }
        }

        private async Task BuildDeleteTables(DbConnection connection)
        {
            var allTables = await GetAllTables(connection);

            var allRelationships = await GetRelationships(connection);

			var dbObjects = BuildObjectsList(allTables, allRelationships);

			_tablesToDelete = dbObjects.tablesToDelete;
			_foreignKeysToDisable = dbObjects.foreignKeysToDisable;

			_deleteSql = DbAdapter.BuildDeleteCommandText(_tablesToDelete, _foreignKeysToDisable);
        }

        private static (string[] tablesToDelete, Constraint[] foreignKeysToDisable) BuildObjectsList(ICollection<string> allTables, IList<Relationship> allRelationships,
            List<string> tablesToDelete = null, List<Constraint> foreignKeysToDisable = null)
        {
            if (tablesToDelete == null)
            {
                tablesToDelete = new List<string>();
            }

			if (foreignKeysToDisable == null)
			{
				foreignKeysToDisable = new List<Constraint>();
			}

            var referencedTables = allRelationships
                .Where(rel => !rel.IsSelfReferencing)
                .Select(rel => rel.PrimaryKeyTable)
                .Distinct()
                .ToList();

            var leafTables = allTables.Except(referencedTables).ToList();

            if (referencedTables.Count > 0 && leafTables.Count == 0)
            {
				//string message = string.Join(",", referencedTables);
				//message = string.Join(Environment.NewLine, $@"There is a dependency involving the DB tables ({message}) and we can't safely build the list of tables to delete.",
				//    "Check for circular references.",
				//    "If you have TablesToIgnore you also need to ignore the tables to which these have primary key relationships.");
				//throw new InvalidOperationException(message);

				foreignKeysToDisable.AddRange(allRelationships.Select(r => r.Constraint).Distinct());
				leafTables = allTables.ToList();

				referencedTables = new List<string>(0);
			}

            tablesToDelete.AddRange(leafTables);

            if (referencedTables.Any())
            {
                var relationships = allRelationships.Where(x => !leafTables.Contains(x.ForeignKeyTable)).ToArray();
                var tables = allTables.Except(leafTables).ToArray();
                BuildObjectsList(tables, relationships, tablesToDelete, foreignKeysToDisable);
            }

			return (tablesToDelete.ToArray(), foreignKeysToDisable.ToArray());
        }

        private async Task<IList<Relationship>> GetRelationships(DbConnection connection)
        {
            var rels = new List<Relationship>();
            var commandText = DbAdapter.BuildRelationshipCommandText(this);

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = commandText;
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var rel = new Relationship
                        {
                            PrimaryKeyTable = $"\"{reader.GetString(0)}\".\"{reader.GetString(1)}\"",
                            ForeignKeyTable = $"\"{reader.GetString(2)}\".\"{reader.GetString(3)}\"",
							Constraint = new Constraint
							{
								ForeignKey = reader.GetString(4),
								ForeignKeyTable = $"\"{reader.GetString(2)}\".\"{reader.GetString(3)}\""
							}
						};

                        rels.Add(rel);
                    }
                }
            }

            return rels;
        }

        private async Task<IList<string>> GetAllTables(DbConnection connection)
        {
            var tables = new List<string>();

            string commandText = DbAdapter.BuildTableCommandText(this);

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = commandText;
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        if (!reader.IsDBNull(0))
                        {
                            tables.Add("\"" + reader.GetString(0) + "\".\"" + reader.GetString(1) + "\"");
                        }
                        else
                        {
                            tables.Add("\"" + reader.GetString(1) + "\"");
                        }
                    }
                }
            }

            return tables.ToList();
        }
    }
}
