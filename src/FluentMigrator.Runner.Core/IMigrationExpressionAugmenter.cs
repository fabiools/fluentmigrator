using FluentMigrator.Infrastructure;

namespace FluentMigrator.Runner;

public interface IMigrationExpressionAugmenter
{
    void Augment(IMigrationContext context);
}
