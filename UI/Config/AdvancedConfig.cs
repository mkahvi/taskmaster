﻿//
// AdvancedConfig.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2019 M.A.
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
	using static Taskmaster;

	public sealed class AdvancedConfig : UI.UniForm
	{
		public AdvancedConfig(bool center = false)
			: base(center)
		{
			Text = "Advanced Configuration";

			var pad = Padding;
			pad.Right = 6;
			Padding = pad;

			using (var corecfg = Config.Load(CoreConfigFilename).BlockUnload())
			{
				AutoSizeMode = AutoSizeMode.GrowAndShrink;
				AutoSize = true;

				var tooltip = new ToolTip();

				var layout = new TableLayoutPanel()
				{
					ColumnCount = 2,
					Dock = DockStyle.Fill,
					AutoSize = true,
					Parent = this,
				};

				var savebutton = new Button()
				{
					Text = "Save",
				};
				savebutton.NotifyDefault(true);

				var cancelbutton = new Button()
				{
					Text = "Cancel",
				};
				cancelbutton.Click += Cancelbutton_Click;

				// USER INTERFACE
				layout.Controls.Add(new AlignedLabel { Text = "User Interface", Font = boldfont, Padding = BigPadding });
				layout.Controls.Add(new EmptySpace());

				var UIUpdateFrequency = new Extensions.NumericUpDownEx()
				{
					Minimum = 100m,
					Maximum = 5000m,
					Unit = "ms",
					DecimalPlaces = 0,
					Increment = 100m,
					Value = 2000m,
				};
				tooltip.SetToolTip(UIUpdateFrequency, "How frequently main UI update happens\nLower values increase CPU usage while UI is visible.");

				UIUpdateFrequency.Value = mainwindow.UIUpdateFrequency;

				layout.Controls.Add(new AlignedLabel { Text = "Refresh frequency", Padding = LeftSubPadding });
				layout.Controls.Add(UIUpdateFrequency);

				// FOREGROUND

				layout.Controls.Add(new AlignedLabel { Text = "Foreground", Font = boldfont, Padding = BigPadding });

				if (ActiveAppMonitorEnabled)
					layout.Controls.Add(new EmptySpace());
				else
					layout.Controls.Add(new AlignedLabel() { Text = "Disabled", Font = boldfont, Padding = BigPadding, ForeColor = System.Drawing.Color.Red });

				var fgHysterisis = new Extensions.NumericUpDownEx()
				{
					Minimum = 200m,
					Maximum = 30000m,
					Unit = "ms",
					DecimalPlaces = 2,
					Increment = 100m,
					Value = 1500m,
				};
				tooltip.SetToolTip(fgHysterisis, "Delay for foreground swapping to take effect.\nLower values make swaps more responsive.\nHigher values make it react less to rapid app swapping.");

				if (ActiveAppMonitorEnabled)
					fgHysterisis.Value = Convert.ToDecimal(activeappmonitor.Hysterisis.TotalMilliseconds);
				else
				{
					using (var cfg = Config.Load(CoreConfigFilename).BlockUnload())
					{
						var perfsec = cfg.Config["Performance"];
						fgHysterisis.Value = perfsec.Get("Foreground hysterisis")?.Int.Constrain(200, 30000) ?? 1500;
					}
				}

				layout.Controls.Add(new AlignedLabel { Text = "Foreground hysterisis", Padding = LeftSubPadding });
				layout.Controls.Add(fgHysterisis);

				// PROCESS MANAGEMENT

				layout.Controls.Add(new AlignedLabel { Text = "Process management", Font = boldfont, Padding = BigPadding });
				layout.Controls.Add(new EmptySpace());

				var IgnoreRecentlyModifiedCooldown = new Extensions.NumericUpDownEx()
				{
					Minimum = 0,
					Maximum = 60,
					Unit = "mins",
					Value = Convert.ToDecimal(Process.Manager.IgnoreRecentlyModified.Value.TotalMinutes),
				};

				layout.Controls.Add(new AlignedLabel { Text = "Ignore recently modified", Padding = LeftSubPadding });
				layout.Controls.Add(IgnoreRecentlyModifiedCooldown);

				var watchlistPowerdown = new Extensions.NumericUpDownEx()
				{
					Minimum = 0m,
					Maximum = 300m,
					Unit = "s",
					DecimalPlaces = 0,
					Increment = 1m,
					Value = 5m,
					Enabled = PowerManagerEnabled,
				};
				tooltip.SetToolTip(watchlistPowerdown, "Delay before rule-based power is returned to normal.\nMostly useful if you frequently close and launch apps with power mode set so there's no powerdown in-between.");

				if (PowerManagerEnabled)
					watchlistPowerdown.Value = Convert.ToDecimal(powermanager.PowerdownDelay.HasValue ? powermanager.PowerdownDelay.Value.TotalSeconds : 0);

				layout.Controls.Add(new AlignedLabel { Text = "Powerdown delay", Padding = LeftSubPadding });
				layout.Controls.Add(watchlistPowerdown);

				// VOLUME METER

				layout.Controls.Add(new AlignedLabel { Text = Constants.VolumeMeter, Font = boldfont, Padding = BigPadding });

				if (AudioManagerEnabled)
					layout.Controls.Add(new EmptySpace());
				else
					layout.Controls.Add(new AlignedLabel() { Text = "Disabled", Font = boldfont, Padding = BigPadding, ForeColor = System.Drawing.Color.Red });

				var volmeter_topmost = new CheckBox();
				var volmeter_show = new CheckBox();

				var volmeter_capout = new Extensions.NumericUpDownEx()
				{
					Unit = "%",
					Maximum = 100,
					Minimum = 20,
					Value = 100,
				};

				var volmeter_capin = new Extensions.NumericUpDownEx()
				{
					Unit = "%",
					Maximum = 100,
					Minimum = 20,
					Value = 100,
				};

				var volmeter_frequency = new Extensions.NumericUpDownEx()
				{
					Unit = "ms",
					Increment = 20,
					Minimum = 10,
					Maximum = 5000,
					Value = 100,
				};

				var volsec = corecfg.Config[Constants.VolumeMeter];
				bool t_volmeter_topmost = volsec.Get("Topmost")?.Bool ?? true;
				int t_volmeter_frequency = volsec.Get("Refresh")?.Int.Constrain(10, 5000) ?? 100;
				int t_volmeter_capoutmax = volsec.Get("Output threshold")?.Int.Constrain(20, 100) ?? 100;
				int t_volmeter_capinmax = volsec.Get("Input threshold")?.Int.Constrain(20, 100) ?? 100;
				bool t_volmeter_show = volsec.Get(Constants.ShowOnStart)?.Bool ?? false;

				volmeter_topmost.Checked = t_volmeter_topmost;
				volmeter_frequency.Value = t_volmeter_frequency;
				volmeter_capout.Value = t_volmeter_capoutmax;
				volmeter_capin.Value = t_volmeter_capinmax;
				volmeter_show.Checked = t_volmeter_show;

				layout.Controls.Add(new AlignedLabel { Text = "Refresh", Padding = LeftSubPadding });
				layout.Controls.Add(volmeter_frequency);
				tooltip.SetToolTip(volmeter_frequency, "Update frequency for the volume bars. Lower is faster.");

				layout.Controls.Add(new AlignedLabel { Text = "Output cap", Padding = LeftSubPadding });
				layout.Controls.Add(volmeter_capout);
				tooltip.SetToolTip(volmeter_capout, "Maximum volume for the bars. Helps make the bars more descriptive in case your software volume is very low.");
				layout.Controls.Add(new AlignedLabel { Text = "Input cap", Padding = LeftSubPadding });
				layout.Controls.Add(volmeter_capin);
				tooltip.SetToolTip(volmeter_capin, "Maximum volume for the bars. Helps make the bars more descriptive in case your software volume is very low.");

				layout.Controls.Add(new AlignedLabel { Text = "Topmost", Padding = LeftSubPadding });
				layout.Controls.Add(volmeter_topmost);
				tooltip.SetToolTip(volmeter_topmost, "Keeps the volume meter over other windows.");

				layout.Controls.Add(new AlignedLabel { Text = Constants.ShowOnStart, Padding = LeftSubPadding });
				layout.Controls.Add(volmeter_show);
				tooltip.SetToolTip(volmeter_show, "Show volume meter on start.");

				// Network

				/*
				layout.Controls.Add(new Label { Text = "Network", Font = boldfont, Padding = BigPadding, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true });

				if (NetworkMonitorEnabled)
					layout.Controls.Add(new Label()); // empty
				else
					layout.Controls.Add(new Label() { Text = "Disabled", Font = boldfont, Padding = BigPadding, ForeColor = System.Drawing.Color.Red, TextAlign = System.Drawing.ContentAlignment.MiddleLeft });

				var netpoll_frequency = new Extensions.NumericUpDownEx()
				{
					Unit = "s",
					Increment = 1,
					Minimum = 1,
					Maximum = 15,
					Value = 15,
				};

				layout.Controls.Add(new Label { Text = "Poll interval", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = true, Padding = LeftSubPadding });
				layout.Controls.Add(netpoll_frequency);
				tooltip.SetToolTip(netpoll_frequency, "Update frequency network device list.");
				*/

				// ---- SAVE --------------------------------------------------------------------------------------------------------

				savebutton.Click += (_, _ea) =>
				{
					// Record for restarts

					var cfg = corecfg.Config;

					var uisec = cfg["User Interface"];
					int uiupdatems = Convert.ToInt32(UIUpdateFrequency.Value).Constrain(100, 5000);
					uisec["Update frequency"].Int = uiupdatems;
					mainwindow.UIUpdateFrequency = uiupdatems;

					var powsec = cfg[HumanReadable.Hardware.Power.Section];

					if (watchlistPowerdown.Value > 0m)
					{
						int powdelay = Convert.ToInt32(watchlistPowerdown.Value);
						if (PowerManagerEnabled)
							powermanager.PowerdownDelay = TimeSpan.FromSeconds(powdelay);
						powsec["Watchlist powerdown delay"].Int = powdelay;
					}
					else
					{
						powsec.TryRemove("Watchlist powerdown delay");
						if (PowerManagerEnabled)
							powermanager.PowerdownDelay = null;
					}

					var perfsec = cfg["Performance"];

					if (IgnoreRecentlyModifiedCooldown.Value > 0M)
					{
						Process.Manager.IgnoreRecentlyModified = TimeSpan.FromMinutes(Convert.ToDouble(IgnoreRecentlyModifiedCooldown.Value));
						perfsec["Ignore recently modified"].Int = Convert.ToInt32(Process.Manager.IgnoreRecentlyModified.Value.TotalMinutes);
					}
					else
					{
						Process.Manager.IgnoreRecentlyModified = null;
						perfsec.TryRemove("Ignore recently modified");
					}

					int fghys = Convert.ToInt32(fgHysterisis.Value);
					perfsec["Foreground hysterisis"].Int = fghys;
					if (ActiveAppMonitorEnabled)
						activeappmonitor.Hysterisis = TimeSpan.FromMilliseconds(fghys);

					volsec["Topmost"].Bool = volmeter_topmost.Checked;

					if (volmeter_capout.Value < 100)
						volsec["Output threshold"].Int = Convert.ToInt32(volmeter_capout.Value);
					else
						volsec.TryRemove("Output threshold");

					if (volmeter_capin.Value < 100)
						volsec["Input threshold"].Int = Convert.ToInt32(volmeter_capin.Value);
					else
						volsec.TryRemove("Input threshold");

					volsec["Refresh"].Int = Convert.ToInt32(volmeter_frequency.Value);

					volsec[Constants.ShowOnStart].Bool = volmeter_show.Checked;

					DialogResult = DialogResult.OK;
					Close();
				};

				savebutton.Anchor = AnchorStyles.Right;
				layout.Controls.Add(savebutton);
				layout.Controls.Add(cancelbutton);
			}
		}

		void Cancelbutton_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}

		public static void Reveal(bool centerOnScreen=false)
		{
			try
			{
				//await Task.Delay(0);
				// this is really horrifying mess
				var power = Taskmaster.powermanager;
				using (var acw = new AdvancedConfig(centerOnScreen))
				{
					var res = acw.ShowDialog();
					if (acw.DialogOK)
					{
						// NOP
					}
					else
					{
						if (Trace) Log.Verbose("<<UI>> Advanced config cancelled.");
					}
				}
			}
			catch (OutOfMemoryException) { throw; }
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
			}
		}
	}
}