﻿//
// ComponentConfigurationWindow.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018–2019 M.A.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using MKAh;
using Serilog;
using System;
using System.Windows.Forms;

namespace Taskmaster.UI.Config
{
	using static Application;

	public class ComponentConfigurationWindow : UniForm
	{
		readonly ToolTip tooltip;

		internal ComponentConfigurationWindow(ModuleManager modules, bool initial = false, bool center = false)
			: base(centerOnScreen: initial || center)
		{
			// TODO: bool initial should be determined here programmatically instead of based on sender

			SuspendLayout();

			// Size = new System.Drawing.Size(220, 360); // width, height

			Text = "Component configuration";

			DialogResult = DialogResult.Abort;

			FormBorderStyle = FormBorderStyle.FixedDialog;

			WindowState = FormWindowState.Normal;
			FormBorderStyle = FormBorderStyle.FixedDialog; // no min/max buttons as wanted
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			bool WMIPolling = true;
			int WMIPollDelay = 2;
			int ScanFrequency = 180;
			bool scan = true;

			if (modules.processmanager is null)
			{
				using var corecfg = Config.Load(CoreConfigFilename);
				var perfsec = corecfg.Config[Constants.Performance];
				WMIPolling = perfsec.Get(Constants.WMIWatcher)?.Bool ?? true;
				WMIPollDelay = (perfsec.Get(Constants.WMIDelay)?.Int ?? 2).Constrain(0, 5);
				ScanFrequency = perfsec.Get(Constants.ScanFrequency)?.Int ?? 180;
			}
			else
			{
				WMIPolling = modules.processmanager.WMIPolling;
				WMIPollDelay = modules.processmanager.WMIPollDelay;
				if (modules.processmanager.ScanFrequency.HasValue)
					ScanFrequency = Convert.ToInt32(modules.processmanager.ScanFrequency.Value.TotalSeconds);
				else
					scan = false;
			}

			var layout = new Extensions.TableLayoutPanel()
			{
				Parent = this,
				ColumnCount = 2,
				AutoSize = true,
				Padding = new Padding(3),
				Dock = DockStyle.Fill,
				//Dock = DockStyle.Top,
				//BackColor = System.Drawing.Color.Aqua,
			};

			tooltip = new ToolTip();

			var audioman = new CheckBox()
			{
				AutoSize = true,
				Dock = DockStyle.Left,
				Checked = AudioManagerEnabled,
			};
			tooltip.SetToolTip(audioman, "Automatically set application mixer volume.");

			layout.Controls.Add(new Extensions.Label { Text = "Audio manager", Padding = BigPadding });
			layout.Controls.Add(audioman);

			var micmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left,
				Checked = MicrophoneManagerEnabled,
			};
			tooltip.SetToolTip(micmon, "Monitor default communications device and keep its volume.\nRequires audio manager to be enabled.");

			audioman.CheckedChanged += (_, _2) => micmon.Enabled = audioman.Checked;

			layout.Controls.Add(new Extensions.Label { Text = "Microphone manager", Padding = BigPadding });
			layout.Controls.Add(micmon);
			micmon.Click += (_, _2) =>
			{
			};

			var netmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			tooltip.SetToolTip(netmon, "Monitor network interface status and report online status.");
			layout.Controls.Add(new Extensions.Label { Text = "Network monitor", Padding = BigPadding });
			layout.Controls.Add(netmon);
			netmon.Checked = NetworkMonitorEnabled;
			netmon.Click += (_, _2) =>
			{
			};

			var procmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			tooltip.SetToolTip(procmon, "Manage processes based on their name. Default feature of Taskmaster and thus can not be disabled.");
			layout.Controls.Add(new Extensions.Label { Text = "Process manager", Padding = BigPadding });
			layout.Controls.Add(procmon);
			procmon.Enabled = false;
			procmon.Checked = ProcessMonitorEnabled;

			layout.Controls.Add(new Extensions.Label() { Text = "Process detection", Padding = BigPadding });
			var ScanOrWMI = new ComboBox()
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Items = { "Scanning", "WMI polling", "Both" },
				Width = 80,
			};
			layout.Controls.Add(ScanOrWMI);
			tooltip.SetToolTip(ScanOrWMI, "Scanning involves getting all procesess and going through the list, which can cause tiny CPU spiking.\nWMI polling sets up system WMI event listener.\nWMI is known to be slow and buggy, though when it performs well, it does it better than scanning in this case.\nSystem WmiPrvSE or similar process may be seen increasing in activity with WMI in use.");

			layout.Controls.Add(new Extensions.Label() { Text = "Scan frequency", Padding = BigPadding });
			var scanfrequency = new Extensions.NumericUpDownEx()
			{
				Unit = "s",
				Minimum = 0,
				Maximum = 360,
				Dock = DockStyle.Left,
				Value = ScanFrequency,
				Width = 60,
			};
			var defaultBackColor = scanfrequency.BackColor;
			scanfrequency.ValueChanged += (_, _2) =>
			{
				if (ScanOrWMI.SelectedIndex == 0 && scanfrequency.Value == 0)
					scanfrequency.Value = 1;

				if (ScanOrWMI.SelectedIndex != 1 && scanfrequency.Value <= 5)
					scanfrequency.BackColor = System.Drawing.Color.LightPink;
				else
					scanfrequency.BackColor = defaultBackColor;
			};
			layout.Controls.Add(scanfrequency);
			tooltip.SetToolTip(scanfrequency, "In seconds. 0 disables. 1-4 are considered invalid values.");
			layout.Controls.Add(new Extensions.Label() { Text = "WMI poll rate", Padding = BigPadding });
			var wmipolling = new Extensions.NumericUpDownEx()
			{
				Minimum = 1,
				Maximum = 5,
				Unit = "s",
				Value = WMIPollDelay.Constrain(1, 5),
				Dock = DockStyle.Left,
				Enabled = false,
				Width = 60,
			};
			layout.Controls.Add(wmipolling);
			tooltip.SetToolTip(wmipolling, "In seconds.");
			ScanOrWMI.SelectedIndexChanged += (_, _2) =>
			{
				scanfrequency.Enabled = ScanOrWMI.SelectedIndex != 1; // 0 or 2
				wmipolling.Enabled = ScanOrWMI.SelectedIndex != 0; // 1 or 2

				if (ScanOrWMI.SelectedIndex == 0) // Not WMI-only
					scanfrequency.Value = ScanFrequency;
				else if (ScanOrWMI.SelectedIndex == 1) // Not Scan-only
					wmipolling.Value = WMIPollDelay;
				else // Both
				{
					scanfrequency.Value = ScanFrequency;
					wmipolling.Value = WMIPollDelay;
				}
			};

			ScanOrWMI.SelectedIndex = initial ? 2 : ((WMIPolling && scan) ? 2 : (WMIPolling ? 1 : 0));

			var powmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left,
				Enabled = true,
				Checked = PowerManagerEnabled,
			};
			tooltip.SetToolTip(powmon, "Manage power mode.\nNot recommended if you already have a power manager.");

			layout.Controls.Add(new Extensions.Label { Text = "Power manager", Padding = BigPadding });
			layout.Controls.Add(powmon);

			var powbehaviour = new ComboBox()
			{
				Items = { HumanReadable.Hardware.Power.AutoAdjust, HumanReadable.Hardware.Power.RuleBased, HumanReadable.Hardware.Power.Manual },
				DropDownStyle = ComboBoxStyle.DropDownList,
				SelectedIndex = 1,
			};

			powbehaviour.Enabled = powmon.Checked;
			powbehaviour.SelectedIndex = (modules.powermanager?.LaunchBehaviour ?? Power.PowerBehaviour.RuleBased) switch
			{
				Power.PowerBehaviour.Auto => 0,
				Power.PowerBehaviour.Manual => 2,
				_ => 1,
			};
			tooltip.SetToolTip(powbehaviour,
				"Auto-adjust = Automatically adjust power mode based on system load or by watchlist rules\n" +
				"Rule-based = Watchlist rules can affect it\n" +
				"Manual = User control only");
			layout.Controls.Add(new Extensions.Label { Text = "Power behaviour", Padding = BigPadding });
			layout.Controls.Add(powbehaviour);

			var fgmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			fgmon.Checked = ActiveAppMonitorEnabled;
			tooltip.SetToolTip(fgmon, "Allow processes and power mode to be managed based on if a process is in the foreground.\nPOWER MODE SWITCHING NOT IMPLEMENTED.");
			layout.Controls.Add(new Extensions.Label { Text = "Foreground manager", Padding = BigPadding });
			layout.Controls.Add(fgmon);

			// NVM monitor
			var nvmmon = new CheckBox()
			{
				AutoSize = true,
				Dock = DockStyle.Left,
				Checked = StorageMonitorEnabled,
				Enabled = false,
			};
			tooltip.SetToolTip(nvmmon, "Monitor non-volatile memory (HDDs, SSDs, etc.)");
			layout.Controls.Add(new Extensions.Label { Text = "NVM monitor", Padding = BigPadding });
			layout.Controls.Add(nvmmon);

			// TEMP monitor
			var tempmon = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			tooltip.SetToolTip(tempmon, "Monitor temp folder.\nNOT YET FULLY IMPLEMENTED.");
			layout.Controls.Add(new Extensions.Label { Text = "TEMP monitor", Padding = BigPadding });
			layout.Controls.Add(tempmon);
			tempmon.Enabled = false;
			tempmon.Checked = MaintenanceMonitorEnabled;

			// PAGING
			var paging = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left,
			};
			tooltip.SetToolTip(tempmon, "Allow paging RAM to page/swap file.\nNOT YET FULLY IMPLEMENTED.");
			layout.Controls.Add(new Extensions.Label { Text = "Allow paging", Padding = BigPadding });
			layout.Controls.Add(paging);
			paging.Checked = PagingEnabled;

			// REGISTER GLOBAL HOTKEYS
			var hotkeys = new CheckBox()
			{
				AutoSize = true,
				Dock = DockStyle.Left,
				Checked = GlobalHotkeys,
			};
			tooltip.SetToolTip(hotkeys, "Register globally accessible hotkeys for certain actions.");
			layout.Controls.Add(new Extensions.Label { Text = "Global hotkeys", Padding = BigPadding });
			layout.Controls.Add(hotkeys);

			// SHOW ON START
			var showonstart = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};
			tooltip.SetToolTip(showonstart, "Show main window on start.");
			layout.Controls.Add(new Extensions.Label { Text = Constants.ShowOnStart, Padding = BigPadding });
			layout.Controls.Add(showonstart);
			showonstart.Checked = ShowOnStart;

			var autodoc = new CheckBox()
			{
				AutoSize = true,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left,
				Checked = HealthMonitorEnabled,
			};
			layout.Controls.Add(new Extensions.Label() { Text = "Health monitor", Padding = BigPadding });
			tooltip.SetToolTip(autodoc, "Variety of other health & problem monitoring.\nCurrently includes low memory detection and attempting to page apps to free some of it.");
			layout.Controls.Add(autodoc);

			// BUTTONS
			var savebutton = new Extensions.Button()
			{
				Text = "Save",
				//AutoSize = true,
				Width = 80,
				Height = 20,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Right
			};
			// l.Controls.Add(savebutton);
			savebutton.Click += (_, _2) =>
			{
				using var cfg = Config.Load(CoreConfigFilename);
				var mainsec = cfg.Config[Constants.Core];
				var opt = mainsec[Constants.Version];
				opt.String = ConfigVersion;
				opt.Comment = "Magical";

				var compsec = cfg.Config[Constants.Components];
				compsec[HumanReadable.System.Process.Section].Bool = procmon.Checked;
				compsec[HumanReadable.Hardware.Audio.Section].Bool = audioman.Checked;
				compsec[HumanReadable.Hardware.Audio.Microphone].Bool = micmon.Checked;
				// compsec["Media"].Bool = mediamon.Checked;
				compsec[HumanReadable.System.Process.Foreground].Bool = fgmon.Checked;
				compsec[Network.Constants.Network].Bool = netmon.Checked;
				compsec[HumanReadable.Hardware.Power.Section].Bool = powmon.Checked;
				compsec[Constants.Maintenance].Bool = tempmon.Checked;
				compsec[Constants.Health].Bool = autodoc.Checked;

				var optsec = cfg.Config[Constants.Options];
				optsec[Constants.Paging].Bool = paging.Checked;

				var powsec = cfg.Config[HumanReadable.Hardware.Power.Section];
				if (powmon.Checked) powsec[Power.Constants.Behaviour].String = powbehaviour.Text.ToLowerInvariant();

				var uisec = cfg.Config[Constants.UserInterface];
				uisec[Constants.ShowOnStart].Bool = showonstart.Checked;

				var perf = cfg.Config[Constants.Performance];
				var freq = (int)scanfrequency.Value;
				if (freq < 5 && freq != 0) freq = 5;
				perf[Constants.ScanFrequency].Int = (ScanOrWMI.SelectedIndex == 1 ? 0 : freq);
				perf[Constants.WMIWatcher].Bool = (ScanOrWMI.SelectedIndex != 0);
				perf[Constants.WMIDelay].Int = ((int)wmipolling.Value);

				var qol = cfg.Config[Constants.QualityOfLife];
				qol["Register global hotkeys"].Bool = hotkeys.Checked;

				cfg.File.Save(force: true);

				DialogResult = DialogResult.OK;

				Close();
			};

			// l.Controls.Add(new Label());

			var endbutton = new Extensions.Button()
			{
				Text = "Cancel",
				//AutoSize = true,
				Width = 80,
				Height = 26,
				//BackColor = System.Drawing.Color.Azure,
				Dock = DockStyle.Left
			};

			// l.Controls.Add(endbutton);
			endbutton.Click += EndButtonClick;

			layout.Controls.Add(savebutton);
			layout.Controls.Add(endbutton);

			AcceptButton = savebutton;
			CancelButton = endbutton;
			savebutton.NotifyDefault(true);
			endbutton.NotifyDefault(false);
			UpdateDefaultButton();

			// Cross-componenty checkbox functionality

			// fgmon.Enabled is bound to procmon.Checked, procmon however is always in use and checkbox disabled so this doesn't matter
			//fgmon.DataBindings.Add("Enabled", procmon, "Checked", false, DataSourceUpdateMode.Never);

			ResumeLayout(performLayout: false);
		}

		void EndButtonClick(object _, EventArgs _2)
		{
			DialogResult = DialogResult.Abort;
			Close();
		}

		public static void Reveal(ModuleManager modules, bool centerOnScreen = false)
		{
			try
			{
				using var comps = new Config.ComponentConfigurationWindow(modules, initial: false, center: centerOnScreen);
				comps.ShowDialog();
				if (comps.DialogOK)
				{
					if (MessageBox.ShowModal("Restart needed", "TM needs to be restarted for changes to take effect.\n\nCancel to do so manually later.", MessageBox.Buttons.AcceptCancel) == MessageBox.ResultType.OK)
					{
						Log.Information("<UI> Restart request");
						UnifiedExit(restart: true);
					}
				}
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				//throw; // bad idea
			}
		}
	}
}