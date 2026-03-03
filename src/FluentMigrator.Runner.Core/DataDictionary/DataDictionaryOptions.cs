namespace FluentMigrator.Runner.DataDictionary;

public sealed class DataDictionaryOptions
{
    /// <summary>
    /// Indica que a feature de dicionário de dados está habilitada. Se falso, o dicionário de dados não será atualizado.
    /// </summary>
    public bool Enabled { get; set; }
    /// <summary>
    /// Indica que o preenchimento do dicionário de dados é obrigatório. Se verdadeiro, uma exceção será lançada se houver colunas sem descrição.
    /// </summary>
    public bool Required { get; set; } = false;
    /// <summary>
    /// Nome do schema onde a tabela de dicionário de dados está localizada. Se nulo, o schema padrão será usado.
    /// </summary>
    public string? SchemaName { get; set; }
    /// <summary>
    /// Nome da tabela do dicionário de dados onde as informações sobre tabelas e colunas serão armazenadas. Essa tabela deve conter pelo menos as
    /// colunas definidas em ColumnTableName, ColumnColumnName e Description.
    /// </summary>
    public string TableName { get; set; }
    /// <summary>
    /// Noma da tabela na tabela de dicionário de dados onde o nome da tabela do banco de dados será armazenado. Essa coluna deve ser do tipo string.
    /// </summary>
    public string ColumnTableName { get; set; }
    /// <summary>
    /// Nome da coluna na tabela de dicionário de dados onde o nome da coluna do banco de dados será armazenado. Essa coluna deve ser do tipo string.
    /// Se o valor for "#", isso indica que a coluna de nome da coluna não é usada, e as informações serão armazenadas apenas por tabela.
    /// </summary>
    public string ColumnColumnName { get; set; }
    /// <summary>
    /// Descricao da coluna na tabela de dicionário de dados onde a descrição da tabela ou coluna do banco de dados será armazenada. Essa coluna deve ser do tipo string.
    /// </summary>
    public string Description { get; set; } 
}
