namespace NetPulseMonitor;

/// <summary>
/// Installs consistent, plain-language mouse-over help. Explicit help already
/// assigned by a screen is preserved; the dictionaries cover shared actions
/// and every data-grid column used by the desktop application.
/// </summary>
internal static class InterfaceHelp
{
    private static readonly IReadOnlyDictionary<string, string> ButtonHelp =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Pause monitoring"] = "Pause Internet sampling without closing NetPulse. Router telemetry, saved history and settings remain available.",
            ["Resume monitoring"] = "Resume the continuous Internet ping and outage monitor.",
            ["Run speed test now"] = "Run a manual download/upload speed test now. Its result is attributed to the LTE PCell and ordered band set currently reported by the router.",
            ["Cancel speed test"] = "Stop the speed test currently in progress. Existing results are kept; an incomplete result is not scored.",
            ["Reset session"] = "Reset only the current Internet-monitoring session counters and graph. Saved LTE history, settings and CSV logs are not deleted.",
            ["Open log folder"] = "Open the local NetPulse data folder containing CSV logs and saved diagnostic exports.",
            ["Apply safely"] = "Apply the current time-period recommendation with band and PCell identity validation. NetPulse restores the previous router state if the guarded validation fails.",
            ["Test current"] = "Run a speed test on the currently connected LTE profile without changing the router lock.",
            ["Check for updates"] = "Check the official GitHub releases for a newer signed NetPulse package. Nothing is installed until you confirm.",
            ["Configure TP-Link router"] = "Open protected local router setup: address, monitoring switch and Windows-protected password storage.",
            ["Reconnect now"] = "Retry the local TP-Link login and immediately refresh router telemetry.",
            ["Restart router"] = "Ask the configured TP-Link router to reboot after confirmation. LTE and Internet will be temporarily unavailable.",
            ["Apply selected"] = "Apply the selected ordered band set and its PCell CID/EARFCN/PCI. The first band is the PCell; B20 + B3 is different from B3 + B20. Validation rolls back an unstable change.",
            ["Scan bands & cells"] = "Run the three-stage discovery: scan each single band for PCells, test every complete PCell identity across supported bands, then record the ordered aggregation sets the modem actually serves. The prior router state is restored at the end.",
            ["Cancel discovery"] = "Stop discovery safely after the current router operation and restore the router state saved before the scan.",
            ["Run controlled"] = "Test every saved lock-ready candidate in the current official-time period. Each profile is applied, validated, measured when stable, graded for success/rollback, and the original router state is restored at the end.",
            ["Cancel experiment"] = "Cancel the candidate test sequence and restore the router state captured before it started.",
            ["Restore automatic"] = "Remove NetPulse band and PCell locks and return both selections to the router's automatic mode.",
            ["Restore automatic selection"] = "Remove the manual PCell and band locks and let the router select them automatically.",
            ["Copy selected lock"] = "Copy the selected ordered band set plus its PCell EARFCN, PCI and CID. No password or private message data is copied.",
            ["Delete selected"] = "Delete only the selected LTE history candidate and its locally stored measurements after confirmation.",
            ["Clear LTE history"] = "Permanently delete all locally stored LTE candidates, time-period measurements and controlled-test grades after confirmation.",
            ["Save profile to history"] = "Save this complete PCell identity as a testable candidate without inventing measurements or a score.",
            ["Apply band lock"] = "Apply only the chosen band selection. Cell Lock remains independent and is not changed.",
            ["Scan this band"] = "Lock and inspect only the entered PCell band, reading the real serving EARFCN, PCI and CID before restoring the previous state.",
            ["Apply PCell lock"] = "Apply the entered CID, EARFCN and PCI as the PCell lock. Band Lock is handled separately.",
            ["Mark read"] = "Mark the selected SIM message as read on the router.",
            ["Mark unread"] = "Mark the selected SIM message as unread on the router.",
            ["Delete"] = "Delete the selected SIM message from the router after confirmation.",
            ["Send SMS"] = "Send the text to the phone number shown above using the router's SIM. The number is normalized with the configured country calling code.",
            ["Refresh messages"] = "Reload inbox, sent messages and drafts directly from the router.",
            ["New SMS"] = "Open an empty conversation composer. Entering a known number joins its existing conversation after country-code normalization.",
            ["Save draft"] = "Store the current recipient and text as a SIM-message draft on the router.",
            ["Save contact"] = "Save or update the local contact name for this normalized phone number; the contact is also exposed to the paired Companion.",
            ["Refresh"] = "Refresh the data shown in this section now.",
            ["Apply"] = "Apply the selected mobile-network mode to the router after validating that the detected model and firmware advertise it.",
            ["Run diagnostics"] = "Measure the local gateway, DNS resolution and IPv4/IPv6 availability without changing router settings.",
            ["Export full ISP evidence"] = "Create a privacy-reviewed technical ZIP from local monitoring evidence. The app shows exactly what will be included before export.",
            ["Why is my connection slow?"] = "Explain the latest gateway, DNS, LTE-signal, latency, loss and speed evidence in plain language.",
            ["Configure protected password"] = "Open TP-Link setup. A remembered password is protected by Windows and is never written to normal settings or logs.",
            ["Configure persistent phone pairing"] = "Open the local Companion service, pairing and Android-download settings. Pairing remains valid until explicitly revoked.",
            ["Enabled on LAN port"] = "Open the active Companion pairing and local-network service settings.",
            ["Save settings"] = "Validate and save the current NetPulse settings locally.",
            ["Test connection"] = "Test authentication and live telemetry against the configured TP-Link router without saving changes first.",
            ["Save and continue"] = "Validate and save these settings, then return to NetPulse.",
            ["Skip for now"] = "Continue without enabling this optional setup. You can configure it later in Settings.",
            ["Copy pairing code"] = "Copy the persistent local pairing URI so it can be transferred to the Companion app.",
            ["Revoke all and regenerate"] = "Invalidate every currently paired phone and create a new persistent pairing secret after confirmation.",
            ["Copy results"] = "Copy all discovery rows as tab-separated text.",
            ["Open discovery log"] = "Open the local CSV containing the full band-and-cell discovery observations.",
            ["Save"] = "Save the current values and close this window.",
            ["Cancel"] = "Close this window without applying unsaved changes.",
            ["Close"] = "Close this results window; saved history and logs are not changed."
        };

    private static readonly IReadOnlyDictionary<string, string> ToggleHelp =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Allow guarded time-aware cell + band optimization"] = "Opt in to automatic LTE optimization. NetPulse considers only complete PCell CID/EARFCN/PCI profiles with sufficient evidence for the current official-time period, observes dwell and daily-change limits, and restores the previous router state if validation fails.",
            ["Allow paired phones on this Wi-Fi/LAN"] = "Start the authenticated Companion service only on the local Wi-Fi/LAN port shown below. It is not exposed as an Internet service by NetPulse.",
            ["Enable live TP-Link monitoring"] = "Enable local read access to supported TP-Link LTE telemetry, SIM messages and router-management features.",
            ["Show password"] = "Temporarily display the router password in this setup window.",
            ["Protect and remember on this Windows PC"] = "Store the router password with Windows credential protection for this Windows user instead of asking at every launch.",
            ["Show connection health score"] = "Show the combined Internet health card on the Dashboard.",
            ["Show measured LTE recommendation"] = "Show the best eligible LTE profile for the current official-time period on the Dashboard.",
            ["Check GitHub for a newer release once per day"] = "Allow one automatic daily check of the official release metadata. Downloads and installation still follow the app's update confirmation flow."
        };

    private static readonly IReadOnlyDictionary<string, string> ColumnHelp =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Rank"] = "Current official-time-period rank: 50% controlled connection reliability without failure/rollback, 25% normalized download and 25% normalized upload. Missing evidence contributes zero. RF is displayed separately and does not affect Rank.",
            ["Band"] = "Ordered served-band profile. The first band is the PCell requested by the lock; following bands are modem-selected SCells. Order matters: B20 + B3 is not B3 + B20.",
            ["Earfcn"] = "PCell EARFCN (E-UTRA Absolute Radio Frequency Channel Number). Together with PCI and CID it identifies the exact lock target.",
            ["Pci"] = "PCell Physical Cell Identity reported by the router. It is not unique by itself and must be used with EARFCN and CID.",
            ["Cid"] = "Serving PCell CID exactly as reported by the router. It is mandatory because the same band/EARFCN/PCI can be served by different cells.",
            ["Score"] = "Independent RF score from measured SINR/SNR (50%), RSRQ (35%) and signal power RSRP (15%) for this time period. Hover an RF value to see every measurement, sub-score and weighted contribution. RF does not affect Rank.",
            ["TestGrade"] = "Controlled tests completed in the current official-time period and the share that required rollback. 0% rollback is green; 100% rollback is deep red.",
            ["Time"] = "Total time this exact ordered band set and PCell identity was observed in the current official-time period.",
            ["Ping"] = "Time-weighted average of successful Internet pings while this exact LTE profile was active in the current period.",
            ["Load"] = "Estimated congestion from observed download speed versus this profile's best result. The asterisk means this is an estimate, not direct tower load telemetry.",
            ["Drops"] = "Confirmed Internet disconnections attributed to this profile: current period first, all periods second (P/A).",
            ["DropRate"] = "Time-weighted confirmed Internet disconnections per connected hour.",
            ["Down"] = "Time-aware average download speed from completed speed tests while this exact profile was active.",
            ["Up"] = "Time-aware average upload speed from completed speed tests while this exact profile was active.",
            ["Confidence"] = "Evidence state for the current period. Basic/Medium/High reflects connected time and speed-test coverage; awaiting/gathering states remain available for controlled trial.",
            ["SmsState"] = "Message state: unread/read inbox item, sent item or draft depending on the selected SMS view.",
            ["SmsFrom"] = "Conversation contact/number, or message recipient in Drafts and Timeline views.",
            ["SmsReceived"] = "Latest conversation activity or the individual message timestamp, shown in official configured time.",
            ["SmsPreview"] = "A short preview of the latest or selected SIM message text.",
            ["Timestamp"] = "Official configured local time when NetPulse recorded the event.",
            ["Kind"] = "Event category such as connectivity, LTE, speed test, SMS or system.",
            ["Message"] = "Human-readable event detail. Sensitive router passwords and SMS contents are not written here.",
            ["DeviceName"] = "Device name reported by the router; it may be blank when the client supplies no hostname.",
            ["DeviceIp"] = "Current private LAN IP address assigned by the router.",
            ["DeviceMac"] = "Hardware MAC address reported by the router for this connected client.",
            ["DeviceConnection"] = "Router-reported connection type, such as Wi-Fi or Ethernet.",
            ["Requested"] = "Single LTE band NetPulse asked the router to use during this discovery step.",
            ["Serving"] = "Ordered serving profile actually reported by the modem after the requested lock; PCell is first and SCells follow.",
            ["Rsrp"] = "Reference Signal Received Power for the observed PCell; less negative is stronger.",
            ["Rsrq"] = "Reference Signal Received Quality; it reflects interference and congestion as well as signal.",
            ["Snr"] = "Signal-to-noise ratio (SINR/SNR) for the serving radio link; higher is better.",
            ["Samples"] = "Number of stable telemetry readings merged into this discovery observation.",
            ["Status"] = "Outcome of the requested band/cell discovery step, including failures or missing serving-cell observations."
        };

    public static void Install(Control root, ToolTip toolTip)
    {
        Apply(root, toolTip);
        Watch(root, toolTip);
    }

    public static ToolTip Install(Form form)
    {
        var toolTip = new ToolTip
        {
            InitialDelay = 350,
            ReshowDelay = 100,
            AutoPopDelay = 16000,
            ShowAlways = true
        };
        Install(form, toolTip);
        form.Disposed += (_, _) => toolTip.Dispose();
        return toolTip;
    }

    public static string ColumnDescription(string name, string header) =>
        ColumnHelp.TryGetValue(name, out string? text)
            ? text
            : $"{header}: values reported or calculated for this row.";

    private static void Watch(Control control, ToolTip toolTip)
    {
        control.ControlAdded += (_, args) =>
        {
            if (args.Control is null)
                return;
            Apply(args.Control, toolTip);
            Watch(args.Control, toolTip);
        };
        foreach (Control child in control.Controls)
            Watch(child, toolTip);
    }

    private static void Apply(Control control, ToolTip toolTip)
    {
        if (control is Button button)
        {
            string key = NormalizeActionText(button.Text);
            if (TryFindByPrefix(ButtonHelp, key, out string? description))
                toolTip.SetToolTip(button, description);
        }
        else if (control is CheckBox checkBox &&
                 ToggleHelp.TryGetValue(checkBox.Text.Trim(), out string? description))
        {
            toolTip.SetToolTip(checkBox, description);
        }
        else if (control is DataGridView grid)
        {
            foreach (DataGridViewColumn column in grid.Columns)
                column.HeaderCell.ToolTipText = ColumnDescription(
                    column.Name,
                    column.HeaderText);
        }

        foreach (Control child in control.Controls)
            Apply(child, toolTip);
    }

    private static bool TryFindByPrefix(
        IReadOnlyDictionary<string, string> values,
        string key,
        out string? description)
    {
        if (values.TryGetValue(key, out description))
            return true;
        foreach ((string candidate, string text) in values)
        {
            if (key.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
            {
                description = text;
                return true;
            }
        }
        description = null;
        return false;
    }

    private static string NormalizeActionText(string text) =>
        text.Trim().TrimEnd('.', '…');
}
