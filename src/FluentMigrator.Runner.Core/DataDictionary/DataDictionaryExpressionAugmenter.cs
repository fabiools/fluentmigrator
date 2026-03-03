using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

using FluentMigrator.Expressions;
using FluentMigrator.Infrastructure;
using FluentMigrator.Model;
using FluentMigrator.Runner.DataDictionary;

using Microsoft.Extensions.Options;

namespace FluentMigrator.Runner;

public sealed class DataDictionaryExpressionAugmenter : IMigrationExpressionAugmenter
{
    private const string TableMarker = "#";

    private readonly IOptions<DataDictionaryOptions> _options;

    public DataDictionaryExpressionAugmenter(IOptions<DataDictionaryOptions> options)
    {
        _options = options;
    }

    public void Augment(IMigrationContext context)
    {
        var opt = _options.Value;
        if (!opt.Enabled) return;

        if (context.QuerySchema is not IMigrationProcessor processor)
            return;

        if (!context.QuerySchema.TableExists(opt.SchemaName, opt.TableName))
            return;

        if (opt.Required)
            ValidateRequiredDescriptions(context.Expressions);

        // Chaves existentes: "TABELA|COLUNA" (COLUNA pode ser "#")
        var existing = LoadExistingKeys(processor, opt);

        // Coleta a partir das expressions
        var pending = CollectRows(context.Expressions)
            .Select(r => NormalizeRow(r))
            .Where(r => !existing.Contains(MakeKey(r.TableName, r.ColumnName)))
            .ToList();

        if (pending.Count == 0)
            return;

        // Gera 1 InsertDataExpression com várias linhas
        var insert = new InsertDataExpression
        {
            SchemaName = opt.SchemaName,
            TableName = opt.TableName
        };

        foreach (var row in pending)
        {
            var data = new InsertionDataDefinition();
            data.Add(new KeyValuePair<string, object?>(opt.ColumnTableName, row.TableName));
            data.Add(new KeyValuePair<string, object?>(opt.ColumnColumnName, row.ColumnName));
            data.Add(new KeyValuePair<string, object?>(opt.Description, row.Description));
            insert.Rows.Add(data);
        }

        context.Expressions.Add(insert);
    }

    private static HashSet<string> LoadExistingKeys(IMigrationProcessor processor, DataDictionaryOptions opt)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var ds = processor.ReadTableData(opt.SchemaName, opt.TableName);
        if (ds.Tables.Count == 0) return set;

        var t = ds.Tables[0];
        if (!t.Columns.Contains(opt.ColumnTableName) || !t.Columns.Contains(opt.ColumnColumnName))
            return set;

        foreach (DataRow r in t.Rows)
        {
            var table = SafeString(r, opt.ColumnTableName);
            var col = SafeString(r, opt.ColumnColumnName);

            if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(col))
                continue;

            set.Add(MakeKey(NormalizeName(table), NormalizeName(col)));
        }

        return set;
    }

    private static IEnumerable<PendingRow> CollectRows(ICollection<IMigrationExpression> expressions)
    {
        foreach (var exp in expressions)
        {
            switch (exp)
            {
                case CreateTableExpression cte:
                    foreach (var r in FromCreateTable(cte))
                        yield return r;
                    break;

                case AlterTableExpression ate:
                    foreach (var r in FromAlterTable(ate))
                        yield return r;
                    break;

                case CreateColumnExpression cce:
                    foreach (var r in FromCreateColumn(cce))
                        yield return r;
                    break;

                case AlterColumnExpression ace:
                    foreach (var r in FromAlterColumn(ace))
                        yield return r;
                    break;
            }
        }
    }

    private static IEnumerable<PendingRow> FromCreateTable(CreateTableExpression cte)
    {
        var table = cte.TableName;

        // descrição da tabela -> COLUNA = "#"
        if (!string.IsNullOrWhiteSpace(cte.TableDescription))
            yield return new PendingRow(table, TableMarker, cte.TableDescription!.Trim());

        // descrições das colunas
        foreach (var col in cte.Columns)
        {
            if (string.IsNullOrWhiteSpace(col.ColumnDescription))
                continue;

            yield return new PendingRow(table, col.Name, col.ColumnDescription!.Trim());
        }
    }

    private static IEnumerable<PendingRow> FromAlterTable(AlterTableExpression ate)
    {
        if (!string.IsNullOrWhiteSpace(ate.TableDescription))
            yield return new PendingRow(ate.TableName, TableMarker, ate.TableDescription!.Trim());
    }

    private static IEnumerable<PendingRow> FromCreateColumn(CreateColumnExpression cce)
    {
        var col = cce.Column;

        if (!string.IsNullOrWhiteSpace(col.ColumnDescription))
            yield return new PendingRow(cce.TableName, col.Name, col.ColumnDescription!.Trim());
    }

    private static IEnumerable<PendingRow> FromAlterColumn(AlterColumnExpression ace)
    {
        var col = ace.Column;

        if (!string.IsNullOrWhiteSpace(col.ColumnDescription))
            yield return new PendingRow(ace.TableName, col.Name, col.ColumnDescription!.Trim());
    }

    private static PendingRow NormalizeRow(PendingRow r)
    {
        // Firebird sem aspas => nomes tendem a ficar uppercase
        var t = NormalizeName(r.TableName);
        var c = r.ColumnName == TableMarker ? TableMarker : NormalizeName(r.ColumnName);
        return new PendingRow(t, c, r.Description);
    }

    private static string NormalizeName(string name) =>
        string.IsNullOrWhiteSpace(name) ? name : name.ToUpperInvariant();

    private static string MakeKey(string table, string col) =>
        $"{table}|{col}";

    private static string SafeString(DataRow row, string colName)
    {
        if (!row.Table.Columns.Contains(colName))
            return "";

        var v = row[colName];
        if (v is null || v == DBNull.Value)
            return "";

        return Convert.ToString(v) ?? "";
    }

    private static void ValidateRequiredDescriptions(ICollection<IMigrationExpression> expressions)
    {
        foreach (var exp in expressions)
        {
            if (exp is CreateTableExpression cte)
            {
                if (string.IsNullOrWhiteSpace(cte.TableDescription))
                    throw new InvalidOperationException(
                        $"Tabela '{cte.TableName}' criada sem descrição no dicionário de dados.");
            }

            if (exp is CreateColumnExpression cce)
            {
                var col = cce.Column;
                if (string.IsNullOrWhiteSpace(col.ColumnDescription))
                    throw new InvalidOperationException(
                        $"Coluna '{cce.TableName}.{col.Name}' criada sem descrição no dicionário de dados.");
            }
        }
    }

    private readonly struct PendingRow
    {
        public PendingRow(string tableName, string columnName, string description)
        {
            TableName = tableName;
            ColumnName = columnName;
            Description = description;
        }

        public string TableName { get; }
        public string ColumnName { get; }
        public string Description { get; }
    }
}
