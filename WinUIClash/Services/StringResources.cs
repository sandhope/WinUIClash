using Microsoft.UI.Xaml;
using System.ComponentModel;

namespace WinUIClash.Services;

public class StringResources : INotifyPropertyChanged
{
    private ResourceDictionary? _rd;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Load(string language)
    {
        var file = language switch
        {
            "zh-CN" => "Strings.zhCN.xaml",
            "en-US" => "Strings.enUS.xaml",
            _ => "Strings.enUS.xaml"
        };

        _rd = new ResourceDictionary
        {
            Source = new Uri($"ms-appx:///Views/{file}", UriKind.Absolute)
        };

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public string Get(string key)
    {
        if (_rd != null && _rd.ContainsKey(key))
        {
            return (string)_rd[key]!;
        }
        return key;
    }

    public string NavDashboard_Content => Get("NavDashboard.Content");
    public string NavProxies_Content => Get("NavProxies.Content");
    public string NavProfiles_Content => Get("NavProfiles.Content");
    public string NavRequests_Content => Get("NavRequests.Content");
    public string NavConnections_Content => Get("NavConnections.Content");
    public string NavResources_Content => Get("NavResources.Content");
    public string NavRules_Content => Get("NavRules.Content");
    public string NavLogs_Content => Get("NavLogs.Content");
    public string NavTools_Content => Get("NavTools.Content");

    public string DashUploadSpeed_Text => Get("DashUploadSpeed.Text");
    public string DashDownloadSpeed_Text => Get("DashDownloadSpeed.Text");
    public string DashTotalUpload_Text => Get("DashTotalUpload.Text");
    public string DashTotalDownload_Text => Get("DashTotalDownload.Text");
    public string DashActiveConnections_Text => Get("DashActiveConnections.Text");
    public string DashRunning_Text => Get("DashRunning.Text");
    public string DashStopped_Text => Get("DashStopped.Text");
    public string DashStarting_Text => Get("DashStarting.Text");
    public string DashStopping_Text => Get("DashStopping.Text");
    public string DashModeRule_Content => Get("DashModeRule.Content");
    public string DashModeGlobal_Content => Get("DashModeGlobal.Content");
    public string DashModeDirect_Content => Get("DashModeDirect.Content");
    public string DashModeRuleDesc_Text => Get("DashModeRuleDesc.Text");
    public string DashModeGlobalDesc_Text => Get("DashModeGlobalDesc.Text");
    public string DashModeDirectDesc_Text => Get("DashModeDirectDesc.Text");
    public string DashTrafficStats_Text => Get("DashTrafficStats.Text");
    public string DashNetworkInfo_Text => Get("DashNetworkInfo.Text");
    public string DashResetTraffic_Content => Get("DashResetTraffic.Content");
    public string DashCheckIp_Content => Get("DashCheckIp.Content");
    public string DashIpChecking_Text => Get("DashIpChecking.Text");
    public string DashIpFailed_Text => Get("DashIpFailed.Text");
    public string DashLocalIp_Text => Get("DashLocalIp.Text");
    public string DashCoreMemory_Text => Get("DashCoreMemory.Text");
    public string DashCoreStart_Text => Get("DashCoreStart.Text");
    public string DashCoreStop_Text => Get("DashCoreStop.Text");
    public string DashRuntime_Text => Get("DashRuntime.Text");
    public string DashOutboundMode_Text => Get("DashOutboundMode.Text");
    public string DashSystemProxy_Text => Get("DashSystemProxy.Text");
    public string DashSystemInfo_Text => Get("DashSystemInfo.Text");
    public string DashRefreshMemory_Content => Get("DashRefreshMemory.Content");
    public string DashCoreVersion_Text => Get("DashCoreVersion.Text");
    public string DashIpLocalFetching_Text => Get("DashIpLocalFetching.Text");
    public string DashIpLocalFailed_Text => Get("DashIpLocalFailed.Text");

    public string SearchPlaceholder_PlaceholderText => Get("SearchPlaceholder.PlaceholderText");
    public string StatusStopped_Text => Get("StatusStopped.Text");
    public string StatusRunning_Text => Get("StatusRunning.Text");
    public string ProxyLabel_Text => Get("ProxyLabel.Text");
    public string CommonUpdateAll_Text => Get("CommonUpdateAll.Text");
    public string CommonUpdate_Content => Get("CommonUpdate.Content");
    public string CommonSearch_PlaceholderText => Get("CommonSearch.PlaceholderText");
    public string CommonAll_Text => Get("CommonAll.Text");
    public string CommonClear_Content => Get("CommonClear.Content");
    public string CommonExport_Content => Get("CommonExport.Content");
    public string CommonRefresh_Content => Get("CommonRefresh.Content");
    public string CommonClose_Content => Get("CommonClose.Content");
    public string CommonCancel_Content => Get("CommonCancel.Content");
    public string CommonConfirm_Content => Get("CommonConfirm.Content");
    public string CommonDelete_Content => Get("CommonDelete.Content");
    public string CommonSave_Content => Get("CommonSave.Content");
    public string CommonCopy_Content => Get("CommonCopy.Content");
    public string CommonSort_Content => Get("CommonSort.Content");
    public string CommonFilter_Text => Get("CommonFilter.Text");
    public string CommonCount_Text => Get("CommonCount.Text");
    public string CommonProviders_Text => Get("CommonProviders.Text");

    public string SettingsLanguage_Text => Get("SettingsLanguage.Text");
    public string SettingsBasicConfig_Text => Get("SettingsBasicConfig.Text");
    public string SettingsApp_Text => Get("SettingsApp.Text");
    public string SettingsTheme_Text => Get("SettingsTheme.Text");
    public string SettingsAbout_Text => Get("SettingsAbout.Text");

    public string ConfigPort_Text => Get("ConfigPort.Text");
    public string ConfigPortDesc_Text => Get("ConfigPortDesc.Text");
    public string ConfigSocksPort_Text => Get("ConfigSocksPort.Text");
    public string ConfigSocksPortDesc_Text => Get("ConfigSocksPortDesc.Text");
    public string ConfigHttpPort_Text => Get("ConfigHttpPort.Text");
    public string ConfigHttpPortDesc_Text => Get("ConfigHttpPortDesc.Text");
    public string ConfigLogLevel_Text => Get("ConfigLogLevel.Text");
    public string ConfigLogLevelDesc_Text => Get("ConfigLogLevelDesc.Text");
    public string ConfigIpv6_Text => Get("ConfigIpv6.Text");
    public string ConfigIpv6Desc_Text => Get("ConfigIpv6Desc.Text");
    public string ConfigAllowLan_Text => Get("ConfigAllowLan.Text");
    public string ConfigAllowLanDesc_Text => Get("ConfigAllowLanDesc.Text");
    public string ConfigUnifiedDelay_Text => Get("ConfigUnifiedDelay.Text");
    public string ConfigUnifiedDelayDesc_Text => Get("ConfigUnifiedDelayDesc.Text");
    public string ConfigTcpConcurrent_Text => Get("ConfigTcpConcurrent.Text");
    public string ConfigTcpConcurrentDesc_Text => Get("ConfigTcpConcurrentDesc.Text");
    public string ConfigFindProcess_Text => Get("ConfigFindProcess.Text");
    public string ConfigFindProcessDesc_Text => Get("ConfigFindProcessDesc.Text");
    public string ConfigExternalController_Text => Get("ConfigExternalController.Text");
    public string ConfigExternalControllerDesc_Text => Get("ConfigExternalControllerDesc.Text");
    public string ConfigApiSecret_Text => Get("ConfigApiSecret.Text");
    public string ConfigApiSecretDesc_Text => Get("ConfigApiSecretDesc.Text");
    public string ConfigApiSecretPlaceholder_PlaceholderText => Get("ConfigApiSecretPlaceholder.PlaceholderText");
    public string ConfigAutoRestart_Text => Get("ConfigAutoRestart.Text");
    public string ConfigAutoRestartDesc_Text => Get("ConfigAutoRestartDesc.Text");
    public string ConfigCoreBinaryPath_Text => Get("ConfigCoreBinaryPath.Text");
    public string ConfigCoreBinaryPathDesc_Text => Get("ConfigCoreBinaryPathDesc.Text");
    public string ConfigCoreBinaryPathPlaceholder_PlaceholderText => Get("ConfigCoreBinaryPathPlaceholder.PlaceholderText");
    public string ConfigBrowse_Content => Get("ConfigBrowse.Content");
    public string ConfigKeepAlive_Text => Get("ConfigKeepAlive.Text");
    public string ConfigKeepAliveDesc_Text => Get("ConfigKeepAliveDesc.Text");
    public string ConfigTestUrl_Text => Get("ConfigTestUrl.Text");
    public string ConfigTestUrlDesc_Text => Get("ConfigTestUrlDesc.Text");
    public string ConfigUserAgent_Text => Get("ConfigUserAgent.Text");
    public string ConfigUserAgentDesc_Text => Get("ConfigUserAgentDesc.Text");

    public string AppLanguage_Text => Get("AppLanguage.Text");
    public string AppLanguageDesc_Text => Get("AppLanguageDesc.Text");
    public string AppMinimizeOnExit_Text => Get("AppMinimizeOnExit.Text");
    public string AppMinimizeOnExitDesc_Text => Get("AppMinimizeOnExitDesc.Text");
    public string AppAutoLaunch_Text => Get("AppAutoLaunch.Text");
    public string AppAutoLaunchDesc_Text => Get("AppAutoLaunchDesc.Text");
    public string AppAutoRun_Text => Get("AppAutoRun.Text");
    public string AppAutoRunDesc_Text => Get("AppAutoRunDesc.Text");
    public string AppAutoRestart_Text => Get("AppAutoRestart.Text");
    public string AppAutoRestartDesc_Text => Get("AppAutoRestartDesc.Text");
    public string AppShowNotifications_Text => Get("AppShowNotifications.Text");
    public string AppShowNotificationsDesc_Text => Get("AppShowNotificationsDesc.Text");
    public string AppAutoCheckUpdate_Text => Get("AppAutoCheckUpdate.Text");
    public string AppAutoCheckUpdateDesc_Text => Get("AppAutoCheckUpdateDesc.Text");
    public string AppSystemProxy_Text => Get("AppSystemProxy.Text");
    public string AppSystemProxyDesc_Text => Get("AppSystemProxyDesc.Text");
    public string AppProxyGuard_Text => Get("AppProxyGuard.Text");
    public string AppProxyGuardDesc_Text => Get("AppProxyGuardDesc.Text");
    public string AppProxyGuardInterval_Text => Get("AppProxyGuardInterval.Text");
    public string AppProxyGuardIntervalDesc_Text => Get("AppProxyGuardIntervalDesc.Text");
    public string AppTunMode_Text => Get("AppTunMode.Text");
    public string AppTunModeDesc_Text => Get("AppTunModeDesc.Text");
    public string AppTunStack_Text => Get("AppTunStack.Text");
    public string AppTunStackDesc_Text => Get("AppTunStackDesc.Text");
    public string AppBypassDomains_Text => Get("AppBypassDomains.Text");
    public string AppBypassDomainsDesc_Text => Get("AppBypassDomainsDesc.Text");
    public string AppBypassPresets_Text => Get("AppBypassPresets.Text");
    public string BypassPresetDefault_Text => Get("BypassPresetDefault.Text");
    public string BypassPresetLan_Text => Get("BypassPresetLan.Text");
    public string BypassPresetChina_Text => Get("BypassPresetChina.Text");
    public string BypassPresetMergeLan_Text => Get("BypassPresetMergeLan.Text");
    public string BypassPresetMergeChina_Text => Get("BypassPresetMergeChina.Text");
    public string AppCloseConnections_Text => Get("AppCloseConnections.Text");
    public string AppCloseConnectionsDesc_Text => Get("AppCloseConnectionsDesc.Text");
    public string AppOnlyStatisticsProxy_Text => Get("AppOnlyStatisticsProxy.Text");
    public string AppOnlyStatisticsProxyDesc_Text => Get("AppOnlyStatisticsProxyDesc.Text");
    public string SettingsBackup_Text => Get("SettingsBackup.Text");
    public string SettingsExportSettings_Text => Get("SettingsExportSettings.Text");
    public string SettingsImportSettings_Text => Get("SettingsImportSettings.Text");
    public string SettingsExportDone_Title => Get("SettingsExportDone.Title");
    public string SettingsExportDone_Text => Get("SettingsExportDone.Text");
    public string SettingsImportDone_Title => Get("SettingsImportDone.Title");
    public string SettingsImportDone_Text => Get("SettingsImportDone.Text");

    public string ThemeMode_Text => Get("ThemeMode.Text");
    public string ThemeAccentColor_Text => Get("ThemeAccentColor.Text");
    public string ThemeCustomColor_Header => Get("ThemeCustomColor.Header");
    public string ThemeApplyCustomColor_Content => Get("ThemeApplyCustomColor.Content");
    public string ThemeUseSystemAccent_Text => Get("ThemeUseSystemAccent.Text");
    public string ThemeUseSystemAccentDesc_Text => Get("ThemeUseSystemAccentDesc.Text");

    public string AboutTitle_Text => Get("AboutTitle.Text");
    public string AboutCheckUpdate_Content => Get("AboutCheckUpdate.Content");
    public string AboutCheckUpdateText_Text => Get("AboutCheckUpdateText.Text");
    public string AboutOpenDataFolder_Content => Get("AboutOpenDataFolder.Content");
    public string AboutOpenDataFolderText_Text => Get("AboutOpenDataFolderText.Text");
    public string AboutCopyVersion_Content => Get("AboutCopyVersion.Content");
    public string AboutCopyVersionText_Text => Get("AboutCopyVersionText.Text");
    public string AboutExportLogs_Content => Get("AboutExportLogs.Content");
    public string AboutExportLogsText_Text => Get("AboutExportLogsText.Text");
    public string AboutExportLogs_Text => Get("AboutExportLogs.Text");
    public string AboutCheckingUpdate_Text => Get("AboutCheckingUpdate.Text");
    public string AboutNoUpdate_Text => Get("AboutNoUpdate.Text");
    public string AboutNewVersion_Text => Get("AboutNewVersion.Text");
    public string AboutUpdateFailed_Text => Get("AboutUpdateFailed.Text");

    public string ConnTitle_Text => Get("ConnTitle.Text");
    public string ConnCloseAll_Text => Get("ConnCloseAll.Text");
    public string ConnExport_Content => Get("ConnExport.Content");
    public string ConnDetail_Text => Get("ConnDetail.Text");
    public string ConnHost_Text => Get("ConnHost.Text");
    public string ConnSource_Text => Get("ConnSource.Text");
    public string ConnDest_Text => Get("ConnDest.Text");
    public string ConnTransfer_Text => Get("ConnTransfer.Text");
    public string ConnChain_Text => Get("ConnChain.Text");
    public string ConnRule_Text => Get("ConnRule.Text");
    public string ConnStartTime_Text => Get("ConnStartTime.Text");
    public string ConnDuration_Text => Get("ConnDuration.Text");
    public string ConnDirect_Text => Get("ConnDirect.Text");

    public string ProfilesTitle_Text => Get("ProfilesTitle.Text");
    public string ProfilesAdd_Content => Get("ProfilesAdd.Content");
    public string ProfilesExpired_Text => Get("ProfilesExpired.Text");
    public string ProfilesRemainingDays_Text => Get("ProfilesRemainingDays.Text");
    public string ProfilesDay_Text => Get("ProfilesDay.Text");

    public string LogsTitle_Text => Get("LogsTitle.Text");
    public string LogsPause_Content => Get("LogsPause.Content");
    public string LogsResume_Content => Get("LogsResume.Content");
    public string LogsExport_Content => Get("LogsExport.Content");
    public string LogsAllLevels_Text => Get("LogsAllLevels.Text");

    public string RequestsTitle_Text => Get("RequestsTitle.Text");

    public string TrayShow_Text => Get("TrayShow.Text");
    public string TrayRun_Text => Get("TrayRun.Text");
    public string TraySystemProxy_Text => Get("TraySystemProxy.Text");
    public string TrayOutboundMode_Text => Get("TrayOutboundMode.Text");
    public string TrayExit_Text => Get("TrayExit.Text");
    public string TrayTunMode_Text => Get("TrayTunMode.Text");
    public string TrayForceGc_Text => Get("TrayForceGc.Text");
    public string TrayRestartCore_Text => Get("TrayRestartCore.Text");

    public string TimeJustNow_Text => Get("TimeJustNow.Text");
    public string TimeMinutesAgo_Text => Get("TimeMinutesAgo.Text");
    public string TimeHoursAgo_Text => Get("TimeHoursAgo.Text");
    public string TimeDaysAgo_Text => Get("TimeDaysAgo.Text");

    public string StatusBarSystemProxy_Text => Get("StatusBarSystemProxy.Text");
    public string SearchBox_PlaceholderText => Get("SearchBox.PlaceholderText");
    public string SearchCategoryPage_Text => Get("SearchCategoryPage.Text");
    public string SearchCategorySettings_Text => Get("SearchCategorySettings.Text");
    public string ProfilesFallbackLabel_Text => Get("ProfilesFallbackLabel.Text");

    public string ConfigPortSettings_Text => Get("ConfigPortSettings.Text");
    public string ConfigFeatureSwitches_Text => Get("ConfigFeatureSwitches.Text");

    public string AppStartupExit_Text => Get("AppStartupExit.Text");
    public string AppProxyBehavior_Text => Get("AppProxyBehavior.Text");
    public string AppOther_Text => Get("AppOther.Text");
    public string AppSilentLaunch_Text => Get("AppSilentLaunch.Text");
    public string AppSilentLaunchDesc_Text => Get("AppSilentLaunchDesc.Text");
    public string AppLanguageSwitchDesc_Text => Get("AppLanguageSwitchDesc.Text");

    public string ThemeSystem_Content => Get("ThemeSystem.Content");
    public string ThemeLight_Content => Get("ThemeLight.Content");
    public string ThemeDark_Content => Get("ThemeDark.Content");

    public string AboutCheckingStatus_Text => Get("AboutCheckingStatus.Text");
    public string AboutUpToDate_Text => Get("AboutUpToDate.Text");
    public string AboutNewVersionFound_Text => Get("AboutNewVersionFound.Text");
    public string AboutNewVersionTitle_Text => Get("AboutNewVersionTitle.Text");
    public string AboutNewVersionContent_Text => Get("AboutNewVersionContent.Text");
    public string AboutGoDownload_Content => Get("AboutGoDownload.Content");
    public string AboutLater_Content => Get("AboutLater.Content");
    public string AboutCheckFailed_Text => Get("AboutCheckFailed.Text");
    public string AboutVersion_Text => Get("AboutVersion.Text");
    public string AboutDescription_Text => Get("AboutDescription.Text");
    public string AboutTechStack_Text => Get("AboutTechStack.Text");
    public string AboutActions_Text => Get("AboutActions.Text");
    public string AboutFlClash_Content => Get("AboutFlClash.Content");
    public string AboutMihomo_Content => Get("AboutMihomo.Content");
    public string AboutWinAppSdk_Content => Get("AboutWinAppSdk.Content");
    public string AboutFramework_Text => Get("AboutFramework.Text");
    public string AboutCore_Text => Get("AboutCore.Text");

    public string ToolsTitle_Text => Get("ToolsTitle.Text");
    public string ToolsSettingsSection_Text => Get("ToolsSettingsSection.Text");
    public string ToolsOtherSection_Text => Get("ToolsOtherSection.Text");
    public string ToolsBasicConfigSub_Text => Get("ToolsBasicConfigSub.Text");
    public string ToolsAppSettingsSub_Text => Get("ToolsAppSettingsSub.Text");
    public string ToolsThemeSettingsSub_Text => Get("ToolsThemeSettingsSub.Text");
    public string ToolsLanguageSub_Text => Get("ToolsLanguageSub.Text");
    public string ToolsAboutSub_Text => Get("ToolsAboutSub.Text");

    public string ProxyTitle_Text => Get("ProxyTitle.Text");
    public string ProxyTestAll_Text => Get("ProxyTestAll.Text");
    public string ProxyTestSummary_Text => Get("ProxyTestSummary.Text");
    public string ProxyTestSummaryFailed_Text => Get("ProxyTestSummaryFailed.Text");

    public string ProfilesImport_Text => Get("ProfilesImport.Text");
    public string ProfilesPasteClipboard_Content => Get("ProfilesPasteClipboard.Content");
    public string ProfilesActive_Text => Get("ProfilesActive.Text");
    public string ProfilesClipboardTitle_Text => Get("ProfilesClipboardTitle.Text");
    public string ProfilesClipboardContent_Text => Get("ProfilesClipboardContent.Text");
    public string ProfilesUrlHeader_Text => Get("ProfilesUrlHeader.Text");
    public string ProfilesNamePlaceholder_Text => Get("ProfilesNamePlaceholder.Text");
    public string ProfilesNameHeader_Text => Get("ProfilesNameHeader.Text");
    public string ProfilesImportUrl_Text => Get("ProfilesImportUrl.Text");
    public string ProfilesImportFile_Text => Get("ProfilesImportFile.Text");
    public string ProfilesImportTitle_Text => Get("ProfilesImportTitle.Text");
    public string ProfilesViewConfig_Text => Get("ProfilesViewConfig.Text");
    public string ProfilesCopyUrl_Text => Get("ProfilesCopyUrl.Text");
    public string ProfilesEditName_Text => Get("ProfilesEditName.Text");
    public string ProfilesEditNameTitle_Text => Get("ProfilesEditNameTitle.Text");
    public string ProfilesEditProfile_Text => Get("ProfilesEditProfile.Text");
    public string ProfilesEditProfileTitle_Text => Get("ProfilesEditProfileTitle.Text");
    public string ProfilesMoveUp_Text => Get("ProfilesMoveUp.Text");
    public string ProfilesMoveDown_Text => Get("ProfilesMoveDown.Text");
    public string ProfilesAutoUpdate_Text => Get("ProfilesAutoUpdate.Text");
    public string ProfilesConfigViewerTitle_Text => Get("ProfilesConfigViewerTitle.Text");
    public string ConfigSaveReload_Text => Get("ConfigSaveReload.Text");

    public string ConnSearchPlaceholder_PlaceholderText => Get("ConnSearchPlaceholder.PlaceholderText");
    public string ConnCountSuffix_Text => Get("ConnCountSuffix.Text");
    public string ConnCloseSimilar_Text => Get("ConnCloseSimilar.Text");
    public string ConnDetailHost_Text => Get("ConnDetailHost.Text");
    public string ConnDetailSource_Text => Get("ConnDetailSource.Text");
    public string ConnDetailDest_Text => Get("ConnDetailDest.Text");
    public string ConnDetailTransfer_Text => Get("ConnDetailTransfer.Text");
    public string ConnDetailProcess_Text => Get("ConnDetailProcess.Text");
    public string ConnDetailDnsMode_Text => Get("ConnDetailDnsMode.Text");
    public string ConnDetailGeoIP_Text => Get("ConnDetailGeoIP.Text");
    public string ConnCopyHost_Text => Get("ConnCopyHost.Text");
    public string ConnCloseThis_Text => Get("ConnCloseThis.Text");
    public string ConnCopyChains_Text => Get("ConnCopyChains.Text");
    public string ConnCopySource_Text => Get("ConnCopySource.Text");

    public string LogsSearchPlaceholder_PlaceholderText => Get("LogsSearchPlaceholder.PlaceholderText");
    public string LogsAutoScroll_Header => Get("LogsAutoScroll.Header");
    public string LogsCountSuffix_Text => Get("LogsCountSuffix.Text");
    public string LogsCopyPayload_Text => Get("LogsCopyPayload.Text");
    public string LogsCopyFull_Text => Get("LogsCopyFull.Text");

    public string RequestsSearchPlaceholder_PlaceholderText => Get("RequestsSearchPlaceholder.PlaceholderText");
    public string RequestsAutoScroll_Header => Get("RequestsAutoScroll.Header");
    public string RequestsCopyHost_Text => Get("RequestsCopyHost.Text");
    public string RequestsCopyRule_Text => Get("RequestsCopyRule.Text");
    public string RequestsCopyProcess_Text => Get("RequestsCopyProcess.Text");
    public string RequestsCopyAll_Text => Get("RequestsCopyAll.Text");

    public string ConnSortNone_Text => Get("ConnSortNone.Text");
    public string ConnSortHost_Text => Get("ConnSortHost.Text");
    public string ConnSortUpload_Text => Get("ConnSortUpload.Text");
    public string ConnSortDownload_Text => Get("ConnSortDownload.Text");
    public string ConnSortDuration_Text => Get("ConnSortDuration.Text");

    public string SubUsed_Text => Get("SubUsed.Text");
    public string SubRemaining_Text => Get("SubRemaining.Text");
    public string SubExpired_Text => Get("SubExpired.Text");
    public string SubHoursRemaining_Text => Get("SubHoursRemaining.Text");
    public string SubDaysRemaining_Text => Get("SubDaysRemaining.Text");

    public string ProxySortDefault_Text => Get("ProxySortDefault.Text");
    public string ProxySortName_Text => Get("ProxySortName.Text");
    public string ProxySortDelay_Text => Get("ProxySortDelay.Text");
    public string ProxySortType_Text => Get("ProxySortType.Text");
    public string ProxySelectBest_Text => Get("ProxySelectBest.Text");
    public string ProxySearch_PlaceholderText => Get("ProxySearch.PlaceholderText");

    public string ProviderTypeProxy_Text => Get("ProviderTypeProxy.Text");
    public string ProviderTypeRule_Text => Get("ProviderTypeRule.Text");
    public string ProfilesDuplicateUrl_Text => Get("ProfilesDuplicateUrl.Text");

    public string ErrorUpdateTitle_Text => Get("ErrorUpdateTitle.Text");
    public string ErrorSyncTitle_Text => Get("ErrorSyncTitle.Text");
    public string ErrorDeleteTitle_Text => Get("ErrorDeleteTitle.Text");
    public string ErrorCloseTitle_Text => Get("ErrorCloseTitle.Text");

    public string ProfilesDeleteConfirmTitle_Text => Get("ProfilesDeleteConfirmTitle.Text");
    public string ProfilesDeleteConfirmContent_Text => Get("ProfilesDeleteConfirmContent.Text");
    public string ConnCloseAllConfirmTitle_Text => Get("ConnCloseAllConfirmTitle.Text");
    public string ConnCloseAllConfirmContent_Text => Get("ConnCloseAllConfirmContent.Text");

    public string ReqSortNone_Text => Get("ReqSortNone.Text");
    public string ReqSortHost_Text => Get("ReqSortHost.Text");
    public string ReqSortTime_Text => Get("ReqSortTime.Text");
    public string ReqSortUpload_Text => Get("ReqSortUpload.Text");
    public string ReqSortDownload_Text => Get("ReqSortDownload.Text");
    public string ReqSortRule_Text => Get("ReqSortRule.Text");
    public string RequestsExport_Content => Get("RequestsExport.Content");
    public string RequestsExportSuccessTitle_Text => Get("RequestsExportSuccessTitle.Text");
    public string RequestsExportSuccessMsg_Text => Get("RequestsExportSuccessMsg.Text");
    public string RequestsExportFailTitle_Text => Get("RequestsExportFailTitle.Text");
    public string RequestsPause_Content => Get("RequestsPause.Content");
    public string RequestsResume_Content => Get("RequestsResume.Content");
    public string RequestsPause_ToolTip => Get("RequestsPause.ToolTip");

    public string ConnPause_Content => Get("ConnPause.Content");
    public string ConnResume_Content => Get("ConnResume.Content");
    public string ConnPause_ToolTip => Get("ConnPause.ToolTip");

    public string ResTitle_Text => Get("ResTitle.Text");
    public string ResCountSuffix_Text => Get("ResCountSuffix.Text");
    public string ResFilterAll_Content => Get("ResFilterAll.Content");
    public string ResFilterProxy_Content => Get("ResFilterProxy.Content");
    public string ResFilterRule_Content => Get("ResFilterRule.Content");
    public string ResSearchPlaceholder_PlaceholderText => Get("ResSearchPlaceholder.PlaceholderText");
    public string ResUpdateAll_Text => Get("ResUpdateAll.Text");
    public string ResEntries_Text => Get("ResEntries.Text");
    public string ResSource_Text => Get("ResSource.Text");
    public string ResGeoUpdate_Text => Get("ResGeoUpdate.Text");
    public string ResGeoUpdateGeoIp_Text => Get("ResGeoUpdateGeoIp.Text");
    public string ResGeoUpdateGeoSite_Text => Get("ResGeoUpdateGeoSite.Text");
    public string ResGeoUpdating_Text => Get("ResGeoUpdating.Text");
    public string ResGeoUpdateSuccess_Text => Get("ResGeoUpdateSuccess.Text");
    public string ResGeoUpdateMmdb_Text => Get("ResGeoUpdateMmdb.Text");
    public string ResGeoUpdateAsn_Text => Get("ResGeoUpdateAsn.Text");
    public string ResUpdating_Text => Get("ResUpdating.Text");
    public string ResUpdateAllDone_Text => Get("ResUpdateAllDone.Text");
    public string ResHealthCheckDone_Text => Get("ResHealthCheckDone.Text");

    public string BasicConfigAppliedTitle_Text => Get("BasicConfigAppliedTitle.Text");
    public string BasicConfigAppliedMsg_Text => Get("BasicConfigAppliedMsg.Text");
    public string ProfilesSyncDoneTitle_Text => Get("ProfilesSyncDoneTitle.Text");
    public string ProfilesSyncAllDoneMsg_Text => Get("ProfilesSyncAllDoneMsg.Text");
    public string ProfilesSwitchedTitle_Text => Get("ProfilesSwitchedTitle.Text");
    public string ProfilesSwitching_Text => Get("ProfilesSwitching.Text");
    public string NetworkChanged_Text => Get("NetworkChanged.Text");
    public string NetworkChangedMsg_Text => Get("NetworkChangedMsg.Text");
    public string NetworkRestored_Text => Get("NetworkRestored.Text");
    public string NetworkRestoredMsg_Text => Get("NetworkRestoredMsg.Text");
    public string ProfilesCopyPath_Text => Get("ProfilesCopyPath.Text");
    public string ProfilesOpenInExplorer_Text => Get("ProfilesOpenInExplorer.Text");
    public string ProfilesDuplicate_Text => Get("ProfilesDuplicate.Text");
    public string ProfilesExportConfig_Text => Get("ProfilesExportConfig.Text");
    public string ProfilesAutoStartTitle_Text => Get("ProfilesAutoStartTitle.Text");
    public string ProfilesAutoStartMsg_Text => Get("ProfilesAutoStartMsg.Text");

    public string RulesTitle_Text => Get("RulesTitle.Text");
    public string RulesSearchPlaceholder_PlaceholderText => Get("RulesSearchPlaceholder.PlaceholderText");
    public string RulesTypeCol_Text => Get("RulesTypeCol.Text");
    public string RulesPayloadCol_Text => Get("RulesPayloadCol.Text");
    public string RulesTargetCol_Text => Get("RulesTargetCol.Text");
    public string RulesAllProxiesFilter_Text => Get("RulesAllProxiesFilter.Text");
    public string RulesAllTypes_Text => Get("RulesAllTypes.Text");
    public string RulesAllProxies_Text => Get("RulesAllProxies.Text");
    public string RulesCopyType_Text => Get("RulesCopyType.Text");
    public string RulesCopyPayload_Text => Get("RulesCopyPayload.Text");
    public string RulesCopyProxy_Text => Get("RulesCopyProxy.Text");
    public string RulesCopyAll_Text => Get("RulesCopyAll.Text");

    public string ProxyCtxTestDelay_Text => Get("ProxyCtxTestDelay.Text");
    public string ProxyCtxSelect_Text => Get("ProxyCtxSelect.Text");
    public string ProxyCtxCopyName_Text => Get("ProxyCtxCopyName.Text");
    public string ProxyCtxCopyType_Text => Get("ProxyCtxCopyType.Text");
    public string ProxyCtxCopyAll_Text => Get("ProxyCtxCopyAll.Text");

    public string AppErrorTitle_Text => Get("AppErrorTitle.Text");
    public string AppErrorContent_Text => Get("AppErrorContent.Text");
    public string AppErrorContinue_Content => Get("AppErrorContinue.Content");
    public string AppErrorExit_Content => Get("AppErrorExit.Content");
    public string AppUpdateFound_Text => Get("AppUpdateFound.Text");
    public string AppUpdateMsg_Text => Get("AppUpdateMsg.Text");

    public string StatusToggle_ToolTip => Get("StatusToggle.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip");
    public string StatusProxyLabel_Text => Get("StatusProxyLabel.Text");
    public string StatusModeRule_Text => Get("StatusModeRule.Text");
    public string StatusModeGlobal_Text => Get("StatusModeGlobal.Text");
    public string StatusModeDirect_Text => Get("StatusModeDirect.Text");

    public string ThemeModeSystem_Text => Get("ThemeModeSystem.Text");
    public string ThemeModeLight_Text => Get("ThemeModeLight.Text");
    public string ThemeModeDark_Text => Get("ThemeModeDark.Text");

    public string ColorBlue_Text => Get("ColorBlue.Text");
    public string ColorCyan_Text => Get("ColorCyan.Text");
    public string ColorGreen_Text => Get("ColorGreen.Text");
    public string ColorOrange_Text => Get("ColorOrange.Text");
    public string ColorRed_Text => Get("ColorRed.Text");
    public string ColorPurple_Text => Get("ColorPurple.Text");
    public string ColorPink_Text => Get("ColorPink.Text");
    public string ColorIndigo_Text => Get("ColorIndigo.Text");

    public string ErrorCoreBinaryNotFound_Text => Get("ErrorCoreBinaryNotFound.Text");
    public string ErrorCoreBinaryNotFoundMsg_Text => Get("ErrorCoreBinaryNotFoundMsg.Text");
    public string ErrorConfigNotFound_Text => Get("ErrorConfigNotFound.Text");
    public string ErrorRegistryAccess_Text => Get("ErrorRegistryAccess.Text");
    public string CoreStartedTitle_Text => Get("CoreStartedTitle.Text");
    public string CoreStartedMsg_Text => Get("CoreStartedMsg.Text");
    public string CoreStoppedTitle_Text => Get("CoreStoppedTitle.Text");
    public string CoreStoppedMsg_Text => Get("CoreStoppedMsg.Text");
    public string ErrorCoreStartFailed_Text => Get("ErrorCoreStartFailed.Text");
    public string ErrorCoreStartFailedMsg_Text => Get("ErrorCoreStartFailedMsg.Text");
    public string ErrorCoreStopFailed_Text => Get("ErrorCoreStopFailed.Text");
    public string ErrorCoreStopFailedMsg_Text => Get("ErrorCoreStopFailedMsg.Text");
    public string CoreCrashedTitle_Text => Get("CoreCrashedTitle.Text");
    public string CoreCrashedMsg_Text => Get("CoreCrashedMsg.Text");
    public string ErrorCoreCrashMaxRestarts_Text => Get("ErrorCoreCrashMaxRestarts.Text");
    public string ErrorLoadProxies_Text => Get("ErrorLoadProxies.Text");
    public string ErrorLoadRules_Text => Get("ErrorLoadRules.Text");
    public string AboutLicense_Text => Get("AboutLicense.Text");

    public string EmptyProxies_Text => Get("EmptyProxies.Text");
    public string EmptyProfiles_Text => Get("EmptyProfiles.Text");
    public string EmptyConnections_Text => Get("EmptyConnections.Text");
    public string EmptyRequests_Text => Get("EmptyRequests.Text");
    public string EmptyResources_Text => Get("EmptyResources.Text");
    public string EmptyRules_Text => Get("EmptyRules.Text");
    public string EmptyLogs_Text => Get("EmptyLogs.Text");

    public string HelpTitle_Text => Get("HelpTitle.Text");
    public string HelpNav_Text => Get("HelpNav.Text");
    public string HelpSearch_Text => Get("HelpSearch.Text");
    public string HelpRefresh_Text => Get("HelpRefresh.Text");
    public string HelpSidebar_Text => Get("HelpSidebar.Text");
    public string HelpMinimize_Text => Get("HelpMinimize.Text");
    public string HelpCoreToggle_Text => Get("HelpCoreToggle.Text");
    public string HelpProxyToggle_Text => Get("HelpProxyToggle.Text");
    public string HelpThemeToggle_Text => Get("HelpThemeToggle.Text");
    public string HelpCloseAllConns_Text => Get("HelpCloseAllConns.Text");
    public string HelpExport_Text => Get("HelpExport.Text");
    public string HelpSettings_Text => Get("HelpSettings.Text");
    public string HelpQuit_Text => Get("HelpQuit.Text");
    public string HelpShowHelp_Text => Get("HelpShowHelp.Text");
    public string HelpCyclePage_Text => Get("HelpCyclePage.Text");
    public string HelpEscape_Text => Get("HelpEscape.Text");
}