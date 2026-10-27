using System.Linq;
using HostAgent.Core.Dashboard;

namespace HostAgent.Desktop;

internal static class RecommendationText
{
    internal static Recommendation Localize(Recommendation card,DashboardSnapshot view,DesktopLocalization language)
    {
        string T(string source,params object?[] args)=>language.Text(source,args);
        return card.Code switch
        {
            "sustained_cpu"=>card with
            {
                Title=T("InvestigateSustainedCPUUtilization"),Evidence=T("CPUAtLeast90ForAtLeast4MinutesWithinTheLast6Minutes"),
                Benefit=T("ReducingUnnecessaryStartupAppsMayFreeUpProcessingCapacityTheBenefitM"),
                Impact=T("DisablingAutomaticStartupMayDelaySynchronizationOrOtherAppFeatures"),Rollback=T("YouCanEnableTheItemAgainInStartupSettings")
            },
            "sustained_memory"=>card with
            {
                Title=T("InvestigateMemoryPressure"),Evidence=T("RAMAtLeast90ForAtLeast4MinutesWithinTheLast6Minutes"),
                Benefit=T("ClosingAnUnnecessaryAppMayFreeUpRAMSaveAnyUnsavedWorkFirst"),
                Impact=T("UninstallingIsNotRequiredFirstReviewAppUsageOnTheDashboard"),Rollback=T("YouCanReopenAClosedAppUnsavedDataMayNotBeRecoverable")
            },
            "analyze_crash"=>card with
            {
                Title=T("InvestigateTheCauseOfAWindowsCrash"),
                Evidence=T("CodeConfidenceRefersToTheStopCodeClassificationNotTheIdentificationO",view.Incidents.First(x=>x.Id==card.IncidentId).Code,view.Incidents.First(x=>x.Id==card.IncidentId).Confidence),
                Benefit=T("ComparingWEREventsWithTheDumpMayHelpNarrowDownTheCauseOfTheCrash"),Impact=T("AutomaticDriverChangesAreUnavailableThisEvidenceDoesNotIdentifyASpec"),
                Rollback=T("ThisDiagnosticCheckIsReadOnlyAndDoesNotChangeWindows")
            },
            var code when code.StartsWith("disk_space:",System.StringComparison.Ordinal)=>Disk(card,view,language),
            _=>card with{Title=T("Recommendations"),Evidence=T("DetailsForThisRecommendationAreUnavailable"),Benefit="—",Impact="—",Rollback="—"}
        };
    }

    private static Recommendation Disk(Recommendation card,DashboardSnapshot view,DesktopLocalization language)
    {
        var disk=view.Disks.First(x=>"disk_space:"+x.Drive==card.Code);
        string T(string source,params object?[] args)=>language.Text(source,args);
        return card with{Title=T("CheckSpaceOnDisk",disk.Drive),Evidence=T("FreeSpaceB",100d*disk.FreeBytes/disk.CapacityBytes,disk.FreeBytes),
            Benefit=T("FreeingUpSpaceMayHelpAppsAndUpdatesAnEstimateOfTheSpeedImprovementIs"),
            Impact=T("StorageSettingsWillShowCandidatesReviewEverySelectionForDeletionPers"),
            Rollback=T("SomeDeletionsAreIrreversibleBackUpPersonalFilesFirst")};
    }
}
