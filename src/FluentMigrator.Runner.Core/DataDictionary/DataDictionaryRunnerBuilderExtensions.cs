using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluentMigrator.Runner.DataDictionary;

public static class DataDictionaryRunnerBuilderExtensions
{
    public static IMigrationRunnerBuilder AddDataDictionary(
            this IMigrationRunnerBuilder builder,
            Action<DataDictionaryOptions> configure)
    {
        builder.Services.Configure(configure);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IMigrationExpressionAugmenter, DataDictionaryExpressionAugmenter>());
        return builder;
    }
}
