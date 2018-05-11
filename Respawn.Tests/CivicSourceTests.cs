using System;
using System.Data.SqlClient;
using System.Linq;
using NPoco;
using Shouldly;
using Xunit;

namespace Respawn.Tests
{
	public class CivicSourceTests : IDisposable
	{
		readonly static string connectionString = @"Server=.\sqlexpress;Initial Catalog=CivicSource_Tests_PapuaNewGuinea;Integrated Security=true";
		readonly SqlConnection connection = new SqlConnection(connectionString);

		public class Foo
		{
			public int Value { get; set; }
		}

		public CivicSourceTests()
		{
			connection.Open();
		}

		[Fact]
		public void ShouldDeleteData()
		{
			Database db = new Database(connection);
			db.Execute("drop table if exists Foo");
			db.Execute("create table Foo (Value [int])");

			db.InsertBulk(Enumerable.Range(0, 100).Select(i => new Foo { Value = i }));

			db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(100);

			var checkpoint = new Checkpoint();
			checkpoint.Reset(connection).Wait();

			db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(0);
		}

		public void Dispose()
		{
			connection.Close();
			connection.Dispose();
		}
	}
}
