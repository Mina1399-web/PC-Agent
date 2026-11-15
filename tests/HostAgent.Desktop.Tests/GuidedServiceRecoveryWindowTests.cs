using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using HostAgent.Core.Models.Audit;
using HostAgent.Core.Models.Remediation;
using HostAgent.Desktop;
using HostAgent.Infrastructure.Administration;
using HostAgent.Infrastructure.Dashboard;
using HostAgent.Infrastructure.Pipes;
using HostAgent.Infrastructure.Security;
using Xunit;

namespace HostAgent.Desktop.Tests;

public sealed class GuidedServiceRecoveryWindowTests
{
    [Fact]
    public Task Cancel_button_does_not_create_approval_or_send_execute() => StaTest.RunAsync(() =>
    {
        var harness = new ReviewHarness();
        var window = harness.CreateWindow();
        try
        {
            window.Show();
            WaitUntil(() => ((TextBlock)window.FindName("ServiceReviewStatus")).Text == TestLocalization.Czech().Text("ServiceReviewReady"));
            var phraseBox = (TextBox)window.FindName("ServiceReviewExactPhrase");
            var phrase = phraseBox.Text;
            Assert.Equal("START SERVICE ReviewTestService WITH NO DEPENDENCIES", phrase);
            Assert.True(phraseBox.IsReadOnly);
            Assert.Equal(TestLocalization.Czech().Text("ServiceReviewPhraseCopyLabel"), AutomationProperties.GetName(phraseBox));
            Assert.Equal(TestLocalization.Czech().Text("ServiceReviewPhraseCopyLabel"), ((TextBlock)window.FindName("ServiceReviewCopyPhraseLabel")).Text);
            Assert.DoesNotContain("Ověřen", TestLocalization.Czech().Text("ServiceReviewReceiptTitle"));
            var input = (TextBox)window.FindName("ServiceReviewConfirmation");
            Assert.Equal(TestLocalization.Czech().Text("ServiceReviewPhraseLabel"), AutomationProperties.GetName(input));
            Assert.NotEqual(AutomationProperties.GetName(phraseBox), AutomationProperties.GetName(input));
            var approve = (Button)window.FindName("ServiceReviewApproveButton");
            Assert.False(approve.IsEnabled);
            input.Text = phrase;
            Assert.True(approve.IsEnabled);

            ((Button)window.FindName("ServiceReviewCancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(window.IsVisible);
            Assert.Equal(1, window.ExitCode);
            Assert.Equal(0, harness.Authority.CreateApprovalCount);
            Assert.Equal(0, harness.ExecuteCalls);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Closing_the_review_window_before_confirmation_is_a_cancel() => StaTest.RunAsync(() =>
    {
        var harness = new ReviewHarness();
        var window = harness.CreateWindow();
        window.Show();
        WaitUntil(() => ((TextBlock)window.FindName("ServiceReviewStatus")).Text == TestLocalization.Czech().Text("ServiceReviewReady"));
        window.Close();
        Assert.Equal(1, window.ExitCode);
        Assert.Equal(0, harness.Authority.CreateApprovalCount);
        Assert.Equal(0, harness.ExecuteCalls);
    });

    [Fact]
    public Task Unknown_execute_result_shows_each_unavailable_receipt_field() => StaTest.RunAsync(() =>
    {
        var harness = new ReviewHarness { SimulateUnknownExecution = true };
        var window = harness.CreateWindow();
        try
        {
            window.Show();
            WaitUntil(() => ((TextBlock)window.FindName("ServiceReviewStatus")).Text == TestLocalization.Czech().Text("ServiceReviewReady"));
            var phrase = ((TextBox)window.FindName("ServiceReviewExactPhrase")).Text;
            ((TextBox)window.FindName("ServiceReviewConfirmation")).Text = phrase;
            ((Button)window.FindName("ServiceReviewApproveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(() => ((TextBlock)window.FindName("ServiceReviewStatus")).Text == TestLocalization.Czech().Text("ServiceReviewFinished"));
            Assert.Equal(1, harness.ExecuteCalls);
            Assert.Equal(Visibility.Visible, ((Border)window.FindName("ServiceReviewReceipt")).Visibility);
            Assert.Contains("Neznámý výsledek", ((TextBlock)window.FindName("ServiceReviewResult")).Text);
            Assert.Contains("neobdrženo", ((TextBlock)window.FindName("ServiceReviewCommandReceipt")).Text);
            Assert.Contains("odpovídající dokončovací záznam", ((TextBlock)window.FindName("ServiceReviewAuditReceipt")).Text);
            Assert.Contains("Zastaveno", ((TextBlock)window.FindName("ServiceReviewServiceReadback")).Text);
            Assert.Contains("Technický detail", ((TextBox)window.FindName("ServiceReviewTechnicalDetail")).Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Window_start_size_stays_within_primary_work_area() => StaTest.RunAsync(() =>
    {
        var window = new ReviewHarness().CreateWindow();
        try
        {
            var workArea = SystemParameters.WorkArea;
            Assert.True(window.Width <= workArea.Width);
            Assert.True(window.Height <= workArea.Height);
            Assert.True(window.MaxWidth <= workArea.Width);
            Assert.True(window.MaxHeight <= workArea.Height);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("cs-CZ", "Záznam událostí Windows — pád služby", "Správce řízení služeb — změna stavu služby")]
    [InlineData("en", "Windows Event Log — service crash", "Service Control Manager — service state change")]
    [InlineData("de", "Windows-Ereignisprotokoll — Dienstausfall", "Dienststeuerungs-Manager — Statusänderung")]
    public Task Evidence_sources_render_as_localized_meaning_and_keep_the_observed_times(
        string languageCode, string eventLogLabel, string serviceStateLabel) => StaTest.RunAsync(() =>
    {
        var localization = TestLocalization.ForLanguage(languageCode);
        var harness = new ReviewHarness();
        var window = harness.CreateWindow(localization);
        try
        {
            window.Show();
            WaitUntil(() => ((TextBlock)window.FindName("ServiceReviewStatus")).Text == localization.Text("ServiceReviewReady"));

            var evidence = ((TextBlock)window.FindName("ServiceReviewEvidence")).Text;
            var expectedEventLog = $"{eventLogLabel} — {ReviewHarness.EvidenceObservedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}";
            var expectedServiceState = $"{serviceStateLabel} — {ReviewHarness.EvidenceObservedAtUtc.AddSeconds(-1).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}";
            Assert.Equal(string.Join(Environment.NewLine, expectedEventLog, expectedServiceState), evidence);
            Assert.DoesNotContain("eventlog.service-crash", evidence);
            Assert.DoesNotContain("service.state_change", evidence);
        }
        finally { window.Close(); }
    });

    private static void WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < until)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(10);
        }
        Assert.True(condition());
    }

    private sealed class ReviewHarness
    {
        private const string IncidentIdValue = "00112233445566778899aabbccddeeff";
        private const string DiagnosisId = "review-window-diagnosis";
        private const string ServiceName = "ReviewTestService";
        private const string ServiceId = "review-test-service";
        private static readonly DateTimeOffset ObservedAt = DateTimeOffset.UtcNow;
        public readonly FakeAuthority Authority = new();
        public int ExecuteCalls;
        public bool SimulateUnknownExecution;
        public static DateTimeOffset EvidenceObservedAtUtc => ObservedAt;

        public GuidedServiceRecoveryWindow CreateWindow(DesktopLocalization? localization = null)
        {
            var workflow = new GuidedServiceRecoveryWorkflow(SendAsync, Authority, readbackTimeout: TimeSpan.Zero);
            return new(workflow, IncidentIdValue, ServiceName, localization ?? TestLocalization.Czech());
        }

        private ValueTask<byte[]> SendAsync(byte[] payload, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var message = AdminProtocol.Parse(payload);
            if (message.Command == "incident")
            {
                return ValueTask.FromResult(AdminProtocol.Reply(true, "incident", new
                {
                    incidentId = IncidentIdValue,
                    state = "Active",
                    diagnosisId = DiagnosisId,
                    confidence = 91,
                    observedAtUtc = ObservedAt,
                    rootCause = new { code = "service_crash", subject = ServiceName },
                    evidence = new[]
                    {
                        new { source = "eventlog.service-crash", observedAtUtc = ObservedAt },
                        new { source = "service.state_change", observedAtUtc = ObservedAt.AddSeconds(-1) }
                    }
                }));
            }
            if (message.Command == "propose")
            {
                var request = AdminProtocol.Proposal(message.Body, DiagnosisId, 91, ["event-crash", "state-change"]);
                using var requestDocument = JsonDocument.Parse(HelperProtocol.SerializeRequest(request));
                return ValueTask.FromResult(AdminProtocol.Reply(true, "confirmation_required", requestDocument.RootElement));
            }
            if (message.Command == "execute")
            {
                ExecuteCalls++;
                if (!SimulateUnknownExecution) throw new InvalidOperationException("cancel test must never dispatch execute");
                return ValueTask.FromResult(AdminProtocol.Reply(false, "control_outcome_uncertain"));
            }
            throw new InvalidDataException("unexpected_admin_command");
        }

        public sealed class FakeAuthority : IGuidedServiceRecoveryAuthority
        {
            public string ApproverSid => "S-1-5-21-111-222-333-1001";
            public int CreateApprovalCount;
            public ValueTask<ServiceControllerStatus> ReadServiceStatusAsync(string serviceName, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult(ServiceControllerStatus.Stopped);
            }
            public ValueTask<IReadOnlyList<ServiceDependencySnapshot>> ReadServiceDependenciesAsync(string serviceName, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult<IReadOnlyList<ServiceDependencySnapshot>>([]);
            }
            public HelperConfiguration LoadConfiguration() => new()
            {
                ReadOnly = false,
                Shadow = false,
                KillSwitch = false,
                ConfigurationAvailable = true,
                ApproverSids = [ApproverSid],
                Services = [new(ServiceId, ServiceName, [HelperOperation.StartService])]
            };
            public bool RemediationDisabled => false;
            public ValueTask<IReadOnlyList<AuditRecord>> ReadAuditAsync(Guid requestId, CancellationToken token) =>
                ValueTask.FromResult<IReadOnlyList<AuditRecord>>([]);
            public ValueTask<OperationApproval> CreateApprovalAsync(HelperOperationRequest request,
                ApprovedTarget displayedTarget, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                CreateApprovalCount++;
                var now = DateTimeOffset.UtcNow;
                return ValueTask.FromResult(new OperationApproval(Guid.NewGuid(), request.RequestId, request.IncidentId,
                    request.DiagnosisId, request.Operation, HelperProtocol.Digest(request), ApproverSid, now,
                    now.AddMinutes(5)) { Target = displayedTarget });
            }
        }
    }
}
