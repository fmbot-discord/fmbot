using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;

namespace FMBot.Persistence
{
    public static class QueryHelper
    {
        DefaultTypeMap.m = true;

        public static SqlConnection CreateSqlConnection(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connectionstring is empty");
            }

            return new SqlConnection(connectionString);
        }

        public static async Task<T> FirstOrDefault<T>(string query, object parameters, string connectionString)
        {
            using (var connection = CreateSqlConnection(connectionString))
            {
                await connection.OpenAsync();
                var result = await connection.QueryAsync<T>(query, parameters);

                return result.FirstOrDefault();
            }
        }

        public static async Task<IReadOnlyList<T>> GetAll<T>(string query, string connectionString)
        {
            using (var connection = CreateSqlConnection(connectionString))
            {
                {
                    await connection.OpenAsync();
                    var result = await connection.QueryAsync<T>(query);

                    return result.ToList();
                }
            }
        }

        public static async Task<IReadOnlyList<T>> GetAll<T>(string query, object parameters, string connectionString)
        {
            using (var connection = CreateSqlConnection(connectionString))
            {
                await connection.OpenAsync();
                var result = await connection.QueryAsync<T>(query, parameters);

                return result.ToList();
            }
        }

        public static async Task ExecuteQuery(string query, object parameters, string connectionString)
        {
            using (var connection = CreateSqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await connection.ExecuteAsync(query, parameters);
            }
        }

    }
}
