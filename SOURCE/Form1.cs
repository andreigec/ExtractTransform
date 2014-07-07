﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ANDREICSLIB;
using ExtractTransform.Controllers;
using ExtractTransform.ServiceReference1;

namespace ExtractTransform
{
	public partial class Form1 : Form
	{
		#region licensing

		private const string AppTitle = "ExtractTransform";
		private const double AppVersion = 0.1;
		private const String HelpString = "";

		private readonly String OtherText =
			@"©" + DateTime.Now.Year +
			@" Andrei Gec (http://www.andreigec.net)

Licensed under GNU LGPL (http://www.gnu.org/)

Zip Assets © SharpZipLib (http://www.sharpdevelop.net/OpenSource/SharpZipLib/)
";

		public Licensing.DownloadedSolutionDetails GetDetails()
		{
			try
			{
				var sr = new ServicesClient();
				var ti = sr.GetTitleInfo(AppTitle);
				if (ti == null)
					return null;
				return ToDownloadedSolutionDetails(ti);

			}
			catch (Exception)
			{
			}
			return null;
		}

		public static Licensing.DownloadedSolutionDetails ToDownloadedSolutionDetails(TitleInfoServiceModel tism)
		{
			return new Licensing.DownloadedSolutionDetails()
			{
				ZipFileLocation = tism.LatestTitleDownloadPath,
				ChangeLog = tism.LatestTitleChangelog,
				Version = tism.LatestTitleVersion
			};
		}

		#endregion



		public Form1()
		{
			InitializeComponent();
		}

		private void Form1_Load(object sender, EventArgs e)
		{

			FileExtras.SaveToFile("test.csv", "sass", true);

			Licensing.CreateLicense(this, menuStrip1,
				new Licensing.SolutionDetails(GetDetails, HelpString, AppTitle, AppVersion, OtherText));

			runCountCB.SelectedIndex = runCountCB.Items.Count - 1;
			waitTimeSeconds.SelectedIndex = 0;
			CheckForIllegalCrossThreadCalls = false;

		}

		private void exitToolStripMenuItem_Click(object sender, EventArgs e)
		{
			Application.Exit();
		}

		private bool Running = false;
		private Thread runThread;

		private void goB_Click(object sender, EventArgs e)
		{
			if (Running)
			{
				ToggleRun(false);
			}
			else
			{
				outputTB.Text="";
				ToggleRun(true);
				try
				{
					//get params from delta combo box
					var @params = GetParams();
					int wait = -1;
					if (int.TryParse(waitTimeSeconds.Text, out wait) == false)
						wait = -1;

					int runCount = -1;
					if (int.TryParse(runCountCB.Text, out runCount) == false)
						runCount = -1;

					var cookies = GetCookies();
					var rc = new RunConfig(urlTB.Text, postTB.Text, filenameTB.Text, @params, runCount, wait, cookies);
					runThread = new Thread(() => RunThread(rc, this));
					runThread.Start();
				}
				catch (Exception ex)
				{
					outputTB.Text = "Error:" + ex;
					ToggleRun(false);
				}
			}
		}

		private List<KeyValuePair<string, string>> GetCookies()
		{
			List<KeyValuePair<string, string>> ret = new List<KeyValuePair<string, string>>();
		
			return ret;

		}

		private void ToggleRun(bool enabled)
		{
			Running = !enabled;
			configbox.Enabled = !enabled;

			if (enabled == false)
			{
				if (runThread != null)
					runThread.Abort();
				runThread = null;
			}

			outputTB.ReadOnly = enabled;
		}

		private List<Param> GetParams()
		{
			var ret = new List<Param>();
			foreach (ListViewItem i in deltaLogicLV.Items)
			{
				var regex = i.Text;
				var change = int.Parse(i.SubItems[1].Text);
				var start = int.Parse(i.SubItems[2].Text);

				var p = new Param(regex, start, change);
				ret.Add(p);
			}
			return ret;
		}


		private static void RunThread(RunConfig rc, Form1 f)
		{
			try
			{
				//keep going until error
				while (true)
				{
					DateTime start = DateTime.Now;
					var res = Controller.Run(rc);

					foreach (var i in res.Items)
					{
						f.outputTB.Text += "\r\n" + i;
					}
					f.outputTB.Text += "\r\n" + string.Format("Runtime(MS):{0}", (DateTime.Now - start).TotalMilliseconds);
					TextboxExtras.ScrollToEnd(f.outputTB);

					if (res.Status == false)
						break;
					//increment params
					var ok = rc.IncrementParams();
					if (ok == false)
						break;
					Thread.Sleep(rc.WaitTimeSec * 1000);
				}
			}
			catch (Exception ex)
			{
			}

			f.Running = false;
			f.configbox.Enabled = true;
		}
	}

	public class Param
	{
		public string Regex;
		public int Start;
		public int Delta;
		public int Current;
		public Param(string regex, int start, int delta)
		{
			Regex = regex;
			Start = start;
			Delta = delta;
			Current = start;
		}
	}

	public class RunConfig
	{
		public List<KeyValuePair<string, string>> Cookies = new List<KeyValuePair<string, string>>();
		private string OrigURL;
		private string OrigPostValues;
		public string OutputFilename { get; private set; }
		private List<Param> Params;
		public int RunCountRemaining { get; private set; }
		private bool RunUntilDone = false;
		public int WaitTimeSec { get; private set; }
		public string ParamsString
		{
			get { return Params.Aggregate("", (a, b) => a + "," + b.Regex + ":" + b.Current); }
		}
		public RunConfig(string url, string postValues, string outputFilename, List<Param> @params, int runCountRemaining, int waitTimeSec, List<KeyValuePair<string, string>> cookies)
		{
			if (runCountRemaining == -1)
				RunUntilDone = true;

			Cookies = cookies;
			RunCountRemaining = runCountRemaining;
			WaitTimeSec = waitTimeSec;
			OrigURL = url;
			OrigPostValues = postValues;
			OutputFilename = outputFilename;
			Params = @params;
		}

		public bool IncrementParams()
		{
			RunCountRemaining--;
			if (RunCountRemaining <= 0 && RunUntilDone == false)
				return false;
			foreach (var p in Params)
			{
				p.Current += p.Delta;
			}
			return true;
		}

		public string URL
		{
			get { return Subst(OrigURL); }
		}

		public string PostValues
		{
			get { return Subst(OrigPostValues); }
		}

		private string Subst(string instr)
		{
			var outstr = instr;
			foreach (var p in Params)
			{
				outstr = outstr.Replace(p.Regex, p.Current.ToString(CultureInfo.InvariantCulture));
			}
			return outstr;
		}
	}
}