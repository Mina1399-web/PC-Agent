using System;
using System.Globalization;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using HostAgent.Infrastructure.Dashboard;

namespace HostAgent.Desktop;

public sealed class GuidedServiceRecoveryWindow : Window
{
    private readonly GuidedServiceRecoveryWorkflow _workflow;
    private readonly DesktopLocalization _localization;
    private readonly string _incidentId;
    private readonly string _serviceName;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StackPanel _details = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _result = new();
    private readonly TextBlock _commandReceipt = new();
    private readonly TextBlock _auditReceipt = new();
    private readonly TextBlock _serviceReadback = new();
    private readonly TextBlock _receiptTitle = new();
    private readonly TextBox _technical = new();
    private readonly TextBox _confirmation = new();
    private readonly Button _approve = new();
    private readonly Button _cancel = new();
    private GuidedServiceRecoveryPreview? _preview;
    private bool _executing;
    private bool _executionFinished;

    public int ExitCode { get; private set; } = 2;

    public GuidedServiceRecoveryWindow(GuidedServiceRecoveryWorkflow workflow, string incidentId,
        string serviceName, DesktopLocalization localization, bool darkTheme = false)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _incidentId = incidentId ?? throw new ArgumentNullException(nameof(incidentId));
        _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        OrbitTheme.ApplyToDialog(this, null, darkTheme);
        Title = T("ServiceReviewTitle");
        var workArea = SystemParameters.WorkArea;
        MaxWidth = Math.Max(320, workArea.Width - 16);
        MaxHeight = Math.Max(320, workArea.Height - 16);
        Width = Math.Min(620, MaxWidth);
        Height = Math.Min(720, MaxHeight);
        MinWidth = Math.Min(540, MaxWidth);
        MinHeight = Math.Min(560, MaxHeight);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        NameScope.SetNameScope(this, new NameScope());
        Content = BuildLayout();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private string T(string key, params object?[] values) => _localization.Text(key, values);
    private SolidColorBrush Brush(string resourceKey) => (SolidColorBrush)FindResource(resourceKey);

    private UIElement BuildLayout()
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        scroll.Content = panel;

        var brand = new Border
        {
            Background = Brush("RailBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 16)
        };
        brand.Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Host Agent", FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = Brush("RailTextBrush") },
                new TextBlock { Text = T("ServiceReviewSubtitle"), Margin = new Thickness(0, 5, 0, 0), Foreground = Brush("RailTextBrush"), TextWrapping = TextWrapping.Wrap }
            }
        };
        panel.Children.Add(brand);
        panel.Children.Add(Card(T("ServiceReviewScope"), "GreenSurfaceBrush", "GreenBrush"));

        _details.Margin = new Thickness(0, 14, 0, 8);
        AddLine(_details, "ServiceReviewIncident", _incidentId);
        AddLine(_details, "ServiceReviewTarget", _serviceName);
        AddLine(_details, "ServiceReviewStatusLabel", T("ServiceReviewPreparing"), "ServiceReviewStatusAtPreview");
        AddLine(_details, "ServiceReviewAction", T("ServiceReviewActionValue"));
        AddLine(_details, "ServiceReviewImpact", T("ServiceReviewImpactPreparing"));
        AddLine(_details, "ServiceReviewDependencies", T("ServiceReviewImpactPreparing"));
        AddLine(_details, "ServiceReviewRegistration", T("ServiceReviewPreparing"));
        AddLine(_details, "ServiceReviewEvidence", T("ServiceReviewPreparing"));
        panel.Children.Add(_details);

        var consent = new Border
        {
            Background = Brush("CardBrush"),
            BorderBrush = Brush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 8, 0, 12)
        };
        var consentPanel = new StackPanel();
        consent.Child = consentPanel;
        consentPanel.Children.Add(new TextBlock { Text = T("ServiceReviewPhrasePrompt"), TextWrapping = TextWrapping.Wrap });
        consentPanel.Children.Add(new TextBlock { Text = T("ServiceReviewPhraseLabel"), Margin = new Thickness(0, 10, 0, 0) });
        _confirmation.Margin = new Thickness(0, 10, 0, 0);
        _confirmation.Padding = new Thickness(9);
        _confirmation.IsEnabled = false;
        _confirmation.Name = "ServiceReviewConfirmation";
        AutomationProperties.SetName(_confirmation, T("ServiceReviewPhraseLabel"));
        _confirmation.TextChanged += (_, _) => UpdateApproveEnabled();
        RegisterName(_confirmation.Name, _confirmation);
        consentPanel.Children.Add(_confirmation);
        var exactPhrase = new TextBox
        {
            Name = "ServiceReviewExactPhrase",
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 9, 0, 0),
            IsReadOnly = true,
            IsTabStop = true,
            Padding = new Thickness(8),
            Background = Brush("CardBrush")
        };
        var phraseCopyLabel = new TextBlock
        {
            Name = "ServiceReviewCopyPhraseLabel",
            Text = T("ServiceReviewPhraseCopyLabel"),
            Margin = new Thickness(0, 12, 0, 0)
        };
        RegisterName(phraseCopyLabel.Name, phraseCopyLabel);
        consentPanel.Children.Add(phraseCopyLabel);
        AutomationProperties.SetName(exactPhrase, T("ServiceReviewPhraseCopyLabel"));
        RegisterName(exactPhrase.Name, exactPhrase);
        consentPanel.Children.Add(exactPhrase);
        panel.Children.Add(consent);

        _status.Text = T("ServiceReviewPreparing");
        _status.Margin = new Thickness(0, 2, 0, 12);
        _status.TextWrapping = TextWrapping.Wrap;
        _status.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        RegisterName("ServiceReviewStatus", _status);
        panel.Children.Add(_status);

        var receipt = new Border
        {
            Background = Brush("AlternateBrush"),
            BorderBrush = Brush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 16),
            Visibility = Visibility.Collapsed
        };
        var receiptPanel = new StackPanel();
        receipt.Child = receiptPanel;
        _receiptTitle.Text = T("ServiceReviewReceiptTitle");
        _receiptTitle.FontWeight = FontWeights.SemiBold;
        _receiptTitle.FontSize = 16;
        receiptPanel.Children.Add(_receiptTitle);
        _result.Name = "ServiceReviewResult";
        _commandReceipt.Name = "ServiceReviewCommandReceipt";
        _auditReceipt.Name = "ServiceReviewAuditReceipt";
        _serviceReadback.Name = "ServiceReviewServiceReadback";
        foreach (var item in new[] { _result, _commandReceipt, _auditReceipt, _serviceReadback })
        {
            item.Margin = new Thickness(0, 6, 0, 0);
            item.TextWrapping = TextWrapping.Wrap;
            RegisterName(item.Name, item);
            receiptPanel.Children.Add(item);
        }
        RegisterName("ServiceReviewReceipt", receipt);
        panel.Children.Add(receipt);

        _technical.TextWrapping = TextWrapping.Wrap;
        _technical.IsReadOnly = true;
        _technical.IsTabStop = true;
        _technical.Padding = new Thickness(7);
        _technical.Margin = new Thickness(0, 0, 0, 12);
        _technical.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(_technical, T("ServiceReviewTechnicalLabel"));
        RegisterName("ServiceReviewTechnicalDetail", _technical);
        panel.Children.Add(_technical);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _approve.Content = T("ServiceReviewApprove");
        _approve.Name = "ServiceReviewApproveButton";
        _approve.Padding = new Thickness(15, 9, 15, 9);
        _approve.Margin = new Thickness(0, 0, 8, 0);
        _approve.IsEnabled = false;
        _approve.Click += ApproveClick;
        RegisterName(_approve.Name, _approve);
        buttons.Children.Add(_approve);
        _cancel.Content = T("ServiceReviewCancel");
        _cancel.Name = "ServiceReviewCancelButton";
        _cancel.Padding = new Thickness(15, 9, 15, 9);
        _cancel.IsCancel = true;
        _cancel.Click += CancelClick;
        RegisterName(_cancel.Name, _cancel);
        buttons.Children.Add(_cancel);
        panel.Children.Add(buttons);
        return scroll;
    }

    private Border Card(string text, string backgroundResource, string accentResource) => new()
    {
        Background = Brush(backgroundResource),
        BorderBrush = Brush(accentResource),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(16),
        Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brush(accentResource) }
    };

    private void AddLine(Panel panel, string labelKey, string value, string? valueName = null)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(new TextBlock { Text = T(labelKey), FontWeight = FontWeights.SemiBold });
        var content = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        row.Children.Add(content);
        RegisterName(valueName ?? labelKey, content);
        panel.Children.Add(row);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _preview = await _workflow.PrepareAsync(_incidentId, _serviceName, _lifetime.Token);
            SetDetail("ServiceReviewStatusAtPreview", T("ServiceReviewStatusAtPreview",
                LocalizedStatus(_preview.StatusAtPreview), FormatUtc(_preview.StatusObservedAtUtc)));
            SetDetail("ServiceReviewAction", T("ServiceReviewActionValue"));
            SetDetail("ServiceReviewImpact", T("ServiceReviewImpactValue", _preview.ServiceName));
            SetDetail("ServiceReviewDependencies", _preview.DependenciesAtPreview.Count == 0
                ? T("ServiceReviewDependenciesEmpty")
                : string.Join(Environment.NewLine, _preview.DependenciesAtPreview.Select(item =>
                    T("ServiceReviewDependencyLine", item.ServiceName, LocalizedStatus(item.Status), FormatUtc(item.ObservedAtUtc)))));
            SetDetail("ServiceReviewRegistration", _preview.ServiceId);
            SetDetail("ServiceReviewEvidence", string.Join(Environment.NewLine, _preview.Evidence.Select(item =>
            $"{LocalizedEvidenceSource(item.Source)} — {FormatUtc(item.ObservedAtUtc)}")));
            ((TextBox)FindName("ServiceReviewExactPhrase")).Text = _preview.ConfirmationPhrase;
            _confirmation.IsEnabled = true;
            _status.Text = T("ServiceReviewReady");
            UpdateApproveEnabled();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (GuidedServiceRecoveryException error)
        {
            _status.Text = T("ServiceReviewUnavailable", LocalizedFailureReason(error.Code));
            SetTechnical(error.Code);
            _approve.IsEnabled = false;
        }
        catch
        {
            _status.Text = T("ServiceReviewUnavailable", T("ServiceReviewReasonUnavailable"));
            SetTechnical("review_unavailable");
            _approve.IsEnabled = false;
        }
    }

    private void SetDetail(string key, string value)
    {
        if (FindName(key) is TextBlock text) text.Text = value;
    }

    private void UpdateApproveEnabled() =>
        _approve.IsEnabled = !_executing && _preview is not null &&
            string.Equals(_confirmation.Text, _preview.ConfirmationPhrase, StringComparison.Ordinal);

    private async void ApproveClick(object sender, RoutedEventArgs e)
    {
        if (_preview is null || !string.Equals(_confirmation.Text, _preview.ConfirmationPhrase, StringComparison.Ordinal)) return;
        _executing = true;
        _approve.IsEnabled = false;
        _confirmation.IsEnabled = false;
        _cancel.IsEnabled = false;
        _status.Text = T("ServiceReviewExecuting");
        try
        {
            var result = await _workflow.ExecuteAsync(_preview, _confirmation.Text, _lifetime.Token);
            ExitCode = result.Outcome switch
            {
                GuidedServiceRecoveryOutcome.Succeeded => 0,
                GuidedServiceRecoveryOutcome.Rejected => 2,
                _ => 3
            };
            var receipt = (Border)FindName("ServiceReviewReceipt");
            receipt.Visibility = Visibility.Visible;
            _receiptTitle.Text = T("ServiceReviewReceiptTitle");
            _result.Text = result.Outcome switch
            {
                GuidedServiceRecoveryOutcome.Succeeded => T("ServiceReviewVerified"),
                GuidedServiceRecoveryOutcome.Rejected => T("ServiceReviewRejected", LocalizedFailureReason(result.Code)),
                _ => T("ServiceReviewUnknown")
            };
            _commandReceipt.Text = T("ServiceReviewCommand", result.HelperResult ?? T("ServiceReviewNoReceipt"));
            _auditReceipt.Text = T(result.AuditRecorded ? "ServiceReviewAuditVerified" : "ServiceReviewAuditUnverified");
            _serviceReadback.Text = T("ServiceReviewReadback", LocalizedStatus(result.ServiceReadback),
                result.ServiceReadbackAtUtc is { } at ? FormatUtc(at) : T("ServiceReviewNoReceipt"));
            if (result.Outcome == GuidedServiceRecoveryOutcome.Succeeded) ClearTechnical();
            else SetTechnical(result.Code);
            _status.Text = T("ServiceReviewFinished");
        }
        catch (OperationCanceledException)
        {
            ShowUnknownReceipt("operation_cancelled_after_dispatch");
        }
        catch (GuidedServiceRecoveryException error)
        {
            ExitCode = 2;
            _status.Text = T("ServiceReviewUnavailable", LocalizedFailureReason(error.Code));
            SetTechnical(error.Code);
        }
        catch (Exception)
        {
            ShowUnknownReceipt("operation_outcome_unverified");
        }
        finally
        {
            _executing = false;
            _executionFinished = true;
            _cancel.IsEnabled = true;
            _cancel.Content = T("ServiceReviewClose");
        }
    }

    private void ShowUnknownReceipt(string code)
    {
        ExitCode = 3;
        ((Border)FindName("ServiceReviewReceipt")).Visibility = Visibility.Visible;
        _receiptTitle.Text = T("ServiceReviewReceiptTitle");
        _result.Text = T("ServiceReviewUnknown");
        _commandReceipt.Text = T("ServiceReviewCommand", T("ServiceReviewNoReceipt"));
        _auditReceipt.Text = T("ServiceReviewAuditUnverified");
        _serviceReadback.Text = T("ServiceReviewReadback", T("ServiceReviewStatusUnknown"), T("ServiceReviewNoReceipt"));
        SetTechnical(code);
        _status.Text = T("ServiceReviewFinished");
    }

    private void SetTechnical(string code)
    {
        var safeCode = new string((code ?? "unknown").Take(128).Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.').ToArray());
        _technical.Text = $"{T("ServiceReviewTechnicalLabel")}: {safeCode}";
        _technical.Visibility = Visibility.Visible;
    }

    private void ClearTechnical()
    {
        _technical.Text = string.Empty;
        _technical.Visibility = Visibility.Collapsed;
    }

    private string LocalizedFailureReason(string code) => code switch
    {
        "incident_not_actionable" or "service_crash_not_corroborated" => T("ServiceReviewReasonIncident"),
        "service_status_unavailable" or "service_is_no_longer_stopped" => T("ServiceReviewReasonStatus"),
        "service_dependency_not_registered" or "service_dependencies_unavailable" or "service_dependency_limit" or
            "service_dependency_policy_mismatch" or "service_dependency_set_changed" or "service_dependency_group_unsupported" or
            "service_dependency_cycle" or "service_dependency_list_malformed" => T("ServiceReviewReasonDependencies"),
        "service_recovery_review_stale" => T("ServiceReviewReasonStale"),
        "approval_disabled" or "helper_configuration_unavailable" or "target_not_registered" => T("ServiceReviewReasonPolicy"),
        "elevated_approver_required" or "untrusted_approver" => T("ServiceReviewReasonPermission"),
        "confirmation_phrase_mismatch" => T("ServiceReviewReasonPhrase"),
        "service_recovery_proposal_unavailable" or "service_recovery_proposal_invalid" => T("ServiceReviewReasonHelper"),
        _ => T("ServiceReviewReasonUnavailable")
    };

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (_executing) return;
        if (!_executionFinished) ExitCode = 1;
        _lifetime.Cancel();
        Close();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_executing)
        {
            e.Cancel = true;
            _status.Text = T("ServiceReviewWaitBeforeClose");
            return;
        }
        if (!_executionFinished && !_lifetime.IsCancellationRequested)
            ExitCode = 1;
        _lifetime.Cancel();
    }

    private string LocalizedStatus(ServiceControllerStatus? status) => status switch
    {
        ServiceControllerStatus.ContinuePending => T("ServiceReviewContinuePending"),
        ServiceControllerStatus.PausePending => T("ServiceReviewPausePending"),
        ServiceControllerStatus.Paused => T("ServiceReviewPaused"),
        ServiceControllerStatus.Running => T("ServiceReviewRunning"),
        ServiceControllerStatus.StartPending => T("ServiceReviewStartPending"),
        ServiceControllerStatus.StopPending => T("ServiceReviewStopPending"),
        ServiceControllerStatus.Stopped => T("ServiceReviewStopped"),
        null => T("ServiceReviewStatusUnknown"),
        _ => T("ServiceReviewStatusUnknown")
    };

    private string LocalizedEvidenceSource(string source) => source switch
    {
        "eventlog.service-crash" => T("ServiceReviewEvidenceEventLog"),
        "service.state_change" => T("ServiceReviewEvidenceServiceState"),
        _ => T("ServiceReviewEvidenceUnknown")
    };

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}

