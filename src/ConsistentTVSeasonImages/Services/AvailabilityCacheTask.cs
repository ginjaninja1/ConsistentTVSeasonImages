using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
namespace ConsistentTVSeasonImages.Services
{
 public sealed class AvailabilityCacheTask : IScheduledTask, IConfigurableScheduledTask, IEarlyRunScheduledTask
 {
  private readonly ProviderImageHelperService service;
  public AvailabilityCacheTask(ProviderImageHelperService service) { this.service = service; }
  public string Name => "Find available season images"; public string Key => "ConsistentSeasonImagesAvailability";
  public string Description => "Checks image providers for missing Emby season posters and banners and refreshes each show's cached opportunities every fourteen days."; public string Category => "GinjaNinja Tools";
  public bool IsHidden => false; public bool IsEnabled => true; public bool IsLogged => true;
  public Task Execute(CancellationToken cancellationToken, IProgress<double> progress) => service.BuildAvailabilityCache(cancellationToken, progress);
  public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[] { new TaskTriggerInfo { Type = "StartupTrigger" }, new TaskTriggerInfo { Type = "IntervalTrigger", IntervalTicks = TimeSpan.FromDays(1).Ticks } };
 }
}
