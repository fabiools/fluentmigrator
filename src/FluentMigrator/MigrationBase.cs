#region License
//
// Copyright (c) 2007-2024, Fluent Migrator Project
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//   http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
#endregion

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Reflection;

using FluentMigrator.Builders.Alter;
using FluentMigrator.Builders.Create;
using FluentMigrator.Builders.IfDatabase;
using FluentMigrator.Builders.Insert;
using FluentMigrator.Builders.Rename;
using FluentMigrator.Builders.Schema;
using FluentMigrator.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace FluentMigrator
{
    /// <summary>
    /// The base migration class
    /// </summary>
    public abstract class MigrationBase : IMigration
    {
        /// <summary>
        /// Gets or sets the migration context
        /// </summary>
        private IMigrationContext _context;

        private readonly object _mutex = new();

        /// <inheritdoc />
        public string ConnectionString { get; protected set; }

        /// <summary>
        /// Gets the migration context
        /// </summary>
        internal IMigrationContext Context => _context ?? throw new InvalidOperationException("The context is not set");

        /// <summary>
        /// Collect the UP migration expressions
        /// </summary>
        public abstract void Up();

        /// <summary>
        /// Collects the DOWN migration expressions
        /// </summary>
        public abstract void Down();

        /// <inheritdoc />
        public virtual void GetUpExpressions(IMigrationContext context)
        {
            lock (_mutex)
            {
                _context = context;
                ConnectionString = context.Connection;
                Up();

                _context = null;
            }
        }

        /// <inheritdoc />
        public virtual void GetDownExpressions(IMigrationContext context)
        {
            lock (_mutex)
            {
                _context = context;

                ConnectionString = context.Connection;
                Down();

                _context = null;
            }
        }

        /// <summary>
        /// Gets the starting point for alterations
        /// </summary>
        public IAlterExpressionRoot Alter => new AlterExpressionRoot(Context);

        /// <summary>
        /// Gets the starting point for creating database objects
        /// </summary>
        public ICreateExpressionRoot Create => new CreateExpressionRoot(Context);

        /// <summary>
        /// Gets the starting point for renaming database objects
        /// </summary>
        public IRenameExpressionRoot Rename => new RenameExpressionRoot(Context);

        /// <summary>
        /// Gets the starting point for data insertion
        /// </summary>
        public IInsertExpressionRoot Insert => new InsertExpressionRoot(Context);

        /// <summary>
        /// Gets the starting point for schema-rooted expressions
        /// </summary>
        public ISchemaExpressionRoot Schema => new SchemaExpressionRoot(Context);

        /// <summary>
        /// Gets the starting point for database specific expressions
        /// </summary>
        /// <param name="databaseType">The supported database types</param>
        /// <returns>The database specific expression</returns>
        public IIfDatabaseExpressionRoot IfDatabase(params string[] databaseType)
        {
            return new IfDatabaseExpressionRoot(Context, databaseType);
        }

        /// <summary>
        /// Gets the starting point for database specific expressions
        /// </summary>
        /// <param name="databaseTypeFunc">The lambda that tests if the expression can be applied to the current database</param>
        /// <returns>The database specific expression</returns>
        public IIfDatabaseExpressionRoot IfDatabase(Predicate<string> databaseTypeFunc)
        {
            return new IfDatabaseExpressionRoot(Context, databaseTypeFunc);
        }

        /// <summary>
        /// Executes the specified SQL query and returns the first column of the first row in the result set, or null if
        /// no rows are returned.
        /// </summary>
        /// <remarks>This method is useful for retrieving single values from the database, such as counts
        /// or sums. Ensure that the SQL query is properly formatted to return a single value.</remarks>
        /// <typeparam name="T">The type of the scalar value to be returned.</typeparam>
        /// <param name="sql">The SQL query to execute. This must be a valid SQL statement that returns a single scalar value.</param>
        /// <returns>The scalar value of type T if the query returns a result; otherwise, null.</returns>
        protected T? SelectScalar<T>(string sql)
        {
            TrySelectScalar<T>(sql, out var value);
            return value;
        }

        /// <summary>
        /// Attempts to execute the specified SQL query and retrieves the first column of the first row in the result
        /// set as a scalar value of the specified type.
        /// </summary>
        /// <remarks>Use this method to efficiently retrieve single values from a database, such as counts
        /// or aggregate results. Ensure that the SQL query is valid and returns a result set with at least one row and
        /// one column. The method returns <see langword="false"/> if the result set is empty or the value is <see
        /// langword="null"/> or <see cref="DBNull.Value"/>.</remarks>
        /// <typeparam name="T">The type of the value to be returned. Must be compatible with the scalar value retrieved from the database.</typeparam>
        /// <param name="sql">The SQL query to execute. The query should return at least one row and one column.</param>
        /// <param name="value">When this method returns <see langword="true"/>, contains the scalar value retrieved from the database;
        /// otherwise, contains the default value for the type.</param>
        /// <returns><see langword="true"/> if a scalar value is successfully retrieved; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown if no <see cref="IMigrationProcessor"/> is available to execute the SQL query.</exception>
        protected bool TrySelectScalar<T>(string sql, out T? value)
        {
            var processor = GetProcessorOrNull();
            if (processor is null)
                throw new InvalidOperationException("No IMigrationProcessor available (Context.QuerySchema is not a processor).");

            // IMigrationProcessor.Read returns DataSet
            var ds = processor.Read(sql);

            if (ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            {
                value = default;
                return false;
            }

            var raw = ds.Tables[0].Rows[0][0];
            if (raw is null || raw == DBNull.Value)
            {
                value = default;
                return false;
            }

            value = ConvertTo<T>(raw);
            return true;
        }

        /// <summary>
        /// Retrieves the migration processor associated with the current context, if available.
        /// </summary>
        /// <remarks>This method first checks whether the <see cref="Context.QuerySchema"/> property
        /// implements <see cref="IMigrationProcessor"/>. If not, it attempts to resolve an <see
        /// cref="IMigrationProcessor"/> from the service provider. This approach supports different dependency
        /// injection configurations and allows for flexible processor retrieval.</remarks>
        /// <returns>An instance of <see cref="IMigrationProcessor"/> if one is found in the current context; otherwise, <see
        /// langword="null"/>.</returns>
        private IMigrationProcessor? GetProcessorOrNull()
        {
            // The common case: QuerySchema is actually the processor
            if (Context.QuerySchema is IMigrationProcessor p)
                return p;

            // Fallback via DI (some setups might register it)
            return Context.ServiceProvider.GetService<IMigrationProcessor>();
        }

        /// <summary>
        /// Converts the specified object to the specified type, handling nullable types, enumerations, GUIDs, and
        /// Boolean values as needed. 
        /// </summary>
        /// <remarks>This method supports conversion for nullable types, enumerations, GUIDs, and Boolean
        /// values, applying appropriate parsing and type conversion as needed. If the conversion is not possible, an
        /// InvalidCastException may be thrown.</remarks>
        /// <typeparam name="T">The type to which the value is converted.</typeparam>
        /// <param name="raw">The object to convert. This can be any type, including null or DBNull.</param>
        /// <returns>An instance of type T that represents the converted value. Returns the default value of T if the input is
        /// null or DBNull.</returns>
        private static T ConvertTo<T>(object raw)
        {
            var targetType = typeof(T);

            // Nullable<T>
            var underlying = Nullable.GetUnderlyingType(targetType);
            if (underlying != null)
            {
                if (raw is null || raw == DBNull.Value)
                    return default!;
                targetType = underlying;
            }

            if (targetType.IsInstanceOfType(raw))
                return (T)raw;

            // Enum
            if (targetType.IsEnum)
            {
                if (raw is string s)
                    return (T)Enum.Parse(targetType, s, ignoreCase: true);

                var enumUnderlying = Enum.GetUnderlyingType(targetType);
                var num = System.Convert.ChangeType(raw, enumUnderlying, CultureInfo.InvariantCulture);
                return (T)Enum.ToObject(targetType, num!);
            }

            // Guid
            if (targetType == typeof(Guid))
            {
                if (raw is Guid g) return (T)(object)g;
                if (raw is string gs) return (T)(object)Guid.Parse(gs);
                if (raw is byte[] bytes) return (T)(object)new Guid(bytes);
            }

            // Bool (Firebird às vezes volta SMALLINT/INTEGER)
            if (targetType == typeof(bool))
            {
                if (raw is bool b) return (T)(object)b;
                if (raw is short sh) return (T)(object)(sh != 0);
                if (raw is int i) return (T)(object)(i != 0);
                if (raw is long l) return (T)(object)(l != 0L);
                if (raw is string bs) return (T)(object)(
                    bs == "1" ||
                    bs.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    bs.Equals("t", StringComparison.OrdinalIgnoreCase) ||
                    bs.Equals("s", StringComparison.OrdinalIgnoreCase)
                );
            }

            return (T)System.Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
        }
    }
}
