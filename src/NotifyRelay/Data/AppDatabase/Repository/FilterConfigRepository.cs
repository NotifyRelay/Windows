using NotifyRelay.Data.AppDatabase.Models;
using NotifyRelay.Services.Filters;

namespace NotifyRelay.Data.AppDatabase.Repository;

public class FilterConfigRepository(DatabaseContext context, ILogger<FilterConfigRepository> logger)
{
    public FilterConfig? Load()
    {
        try
        {
            var entity = context.Database.Find<FilterConfigEntity>(1);
            if (entity == null) return null;
            return DatabaseContext.ConfigFromEntity(entity);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "加载过滤配置失败");
            return null;
        }
    }

    public void Save(FilterConfig config)
    {
        try
        {
            var entity = DatabaseContext.EntityFromConfig(config);
            context.Database.InsertOrReplace(entity);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "保存过滤配置失败");
        }
    }

    public FilterConfig LoadOrCreateDefault()
    {
        var config = Load();
        if (config != null) return config;

        config = new FilterConfig();
        Save(config);
        return config;
    }
}
