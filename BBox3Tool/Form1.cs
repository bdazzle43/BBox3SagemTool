﻿using System;
using System.ComponentModel;
using System.Windows.Forms;
using System.Text;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml;
using System.Collections.Generic;
using BBox3Tool.objects;
using System.Net.NetworkInformation;
using System.Globalization;
using System.Net;

namespace BBox3Tool
{
    public partial class Form1 : Form
    {
        private IModemSession _session;
        private List<ProximusLineProfile> _profiles;

        private Modem _selectedModem = Modem.unknown;

        private Color _colorSelected = Color.FromArgb(174, 204, 237);
        private Color _colorMouseOver = Color.FromArgb(235, 228, 241);

        private readonly Uri _liveUpdateCheck = new Uri("http://www.cloudscape.be/userbasepyro85/latest.xml");
        private readonly Uri _liveUpdateProfiles = new Uri("http://www.cloudscape.be/userbasepyro85/profiles.xml");

        public Form1()
        {
            InitializeComponent();

            //set form title
            this.Text += " " + Application.ProductVersion;

            //set worker thread properties
            backgroundWorker.WorkerSupportsCancellation = true;
            backgroundWorker.WorkerReportsProgress = true;

            //load embedded xml profiles
            _profiles = loadEmbeddedProfiles();

            //do live update, update profiles
            backgroundWorkerLiveUpdate.RunWorkerAsync();
            
            //detect device
            _selectedModem = detectDevice();

            //load settings if saved
            if (loadSettings())
                checkBoxSave.Checked = true;
            else
                checkBoxSave.Checked = false;
            
            //preselect modem
            switch (_selectedModem)
            {
                case Modem.BBOX3S:
                    panelThumb_Click(panelBBox3S, null);
                    break;
                case Modem.BBOX2:
                    panelThumb_Click(panelBBox2, null);
                    break;
                case Modem.FritzBox7390:
                    panelThumb_Click(panelFritzBox, null);
                    break;
                case Modem.BBOX3T:
                    panelUnsupported.Visible = true;
                    break;
                case Modem.unknown:
                default:
                    break;
            }
        }

        //buttons
        //-------
        private void buttonConnect_Click(object sender, EventArgs e)
        {
            //set session
            switch (_selectedModem)
            {
                case Modem.BBOX3S:
                    _session = new Bbox3Session(backgroundWorker, _profiles);
                    break;
                case Modem.BBOX2:
                    _session = new Bbox2Session();
                    break;
                case Modem.FritzBox7390:
                    _session = new FritzBoxSession();
                    break;
                case Modem.BBOX3T:
                    _session = null; 
                    break;
                case Modem.unknown:
                default:
                    _session = null; 
                    break;
            }

            if (_session == null)
            {
                MessageBox.Show("Please select a modem.", "Connection failure", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            //check mode
            bool debug = (textBoxUsername.Text.ToLower() == "debug");
            if (debug)
                initDebugMode();
            else
                initNormalMode();
        }

        private void initDebugMode()
        {
            //get textbox values
            string host = textBoxIpAddress.Text;
            string username = "User"; //overwrite textbox value
            string password = textBoxPassword.Text;

            //init session
            _session = new Bbox3Session(backgroundWorker, _profiles, true);
            if (_session.OpenSession(host, username, password))
            {
                buttonConnect.Enabled = false;
                panelDebug.Visible = true;
                panelInfo.Visible = false;
                panelLogin.Visible = false;
            }
            else
            {
                MessageBox.Show("Could not connect to modem.", "Connection failure", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void initNormalMode()
        {
            //get textbox values
            string host = textBoxIpAddress.Text;
            string username = textBoxUsername.Text;
            string password = textBoxPassword.Text;

            //init session
            if (_session.OpenSession(host, username, password))
            {
                //check remember settings
                if (checkBoxSave.Checked)
                    saveSettings(username, password, host);
                else
                    deleteSettings();
                
                buttonClipboard.Enabled = false;
                buttonConnect.Enabled = false;
                buttonCancel.Enabled = true;

                panelDebug.Visible = false;
                panelInfo.Visible = true;
                panelLogin.Visible = false;

                backgroundWorker.RunWorkerAsync();

                if (_session is Bbox3Session)
                    distanceLabel.Text += "\r\n(experimental)";
            }
            else
            {
                MessageBox.Show("Login incorrect.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            if (backgroundWorker.IsBusy)
            {
                backgroundWorker.CancelAsync();
                buttonCancel.Enabled = false;
            }
        }

        private void buttonClipboard_Click(object sender, EventArgs e)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("[code]");
            builder.AppendLine("B-Box 3 Sagem Tool v" + Application.ProductVersion);
            builder.AppendLine("--------------------" + new String('-', Application.ProductVersion.Length));
            builder.AppendLine("Device:                        " + _session.DeviceName);
            builder.AppendLine("");
            builder.AppendLine("Downstream current bit rate:   " + (_session.DownstreamCurrentBitRate < 0 ? "unknown" : _session.DownstreamCurrentBitRate.ToString("###,###,##0 'kbps'")));
            builder.AppendLine("Upstream current bit rate:     " + (_session.UpstreamCurrentBitRate < 0 ? "unknown" : _session.UpstreamCurrentBitRate.ToString("###,###,##0 'kbps'")));
            builder.AppendLine("");
            
            builder.AppendLine("Downstream max bit rate:       " + (_session.DownstreamMaxBitRate < 0 ? "unknown" : _session.DownstreamMaxBitRate.ToString("###,###,##0 'kbps'")));
            if (_session is Bbox3Session || _session is FritzBoxSession)
                builder.AppendLine("Upstream max bit rate:         " + (_session.UpstreamMaxBitRate < 0 ? "unknown" : _session.UpstreamMaxBitRate.ToString("###,###,##0 'kbps'")));
            builder.AppendLine("");
            
            builder.AppendLine("Downstream attenuation:        " + (_session.DownstreamAttenuation < 0 ? "unknown" : _session.DownstreamAttenuation.ToString("0.0 'dB'")));
            if (_session is Bbox3Session && new List<DSLStandard> { DSLStandard.ADSL, DSLStandard.ADSL2, DSLStandard.ADSL2plus }.Contains(_session.DSLStandard))
                builder.AppendLine("Upstream attenuation:          " + (_session.UpstreamAttenuation < 0 ? "unknown" : _session.UpstreamAttenuation.ToString("0.0 'dB'")));
            builder.AppendLine("");
            
            builder.AppendLine("Downstream noise margin:       " + (_session.DownstreamNoiseMargin < 0 ? "unknown" : _session.DownstreamNoiseMargin.ToString("0.0 'dB'")));
            if (_session is Bbox3Session || _session is FritzBoxSession)
                builder.AppendLine("Upstream noise margin:         " + (_session.UpstreamNoiseMargin < 0 ? "unknown" : _session.UpstreamNoiseMargin.ToString("0.0 'dB'")));
            builder.AppendLine("");

            builder.AppendLine("DSL standard:                  " + _session.DSLStandard.ToString().Replace("plus", "+"));
            if (_session.DSLStandard == DSLStandard.VDSL2)
            {
                ProximusLineProfile currentProfile = getProfile(_session.UpstreamCurrentBitRate, _session.DownstreamCurrentBitRate, _session.Vectoring, _session.Distance);
                if (currentProfile == null)
                {
                    builder.AppendLine("VDSL2 profile:                 unknown");
                    builder.AppendLine("Vectoring:                     unknown");
                    builder.AppendLine("Proximus profile:              unknown");
                    builder.AppendLine("DLM:                           unknown");
                    builder.AppendLine("Repair:                        unknown");
                }
                else
                {

                    builder.AppendLine("VDSL2 profile:                 " + currentProfile.ProfileVDSL2.ToString().Replace("p", ""));
                    //builder.AppendLine("Vectoring:                     " + (_session.VectoringEnabled ? "Yes" : "No"));
                    builder.AppendLine("Vectoring:                     " + (currentProfile.VectoringEnabled ? "Yes" : "No"));
                    builder.AppendLine("Proximus profile:              " + currentProfile.Name);
                    builder.AppendLine("DLM:                           " + (currentProfile.DlmProfile ? "Yes" : "No"));
                    builder.AppendLine("Repair:                        " + (currentProfile.RepairProfile ? "Yes" : "No"));
                }
            }

            if (_session is Bbox3Session)
                builder.AppendLine("Distance (experimental):       " + (_session.Distance == null ? "unknown" : ((decimal)_session.Distance).ToString("0 'm'")));
            else
                builder.AppendLine("Distance                       " + (_session.Distance == null ? "unknown" : ((decimal)_session.Distance).ToString("0 'm'")));

            builder.AppendLine("[/code]");

            Clipboard.SetText(builder.ToString());
        }

        //debug
        private void buttonDebug_Click(object sender, EventArgs e)
        {
            textBoxDebugResult.Text = _session.GetDebugValue(textBoxDebug.Text);
        }

        //debug textbox on enter --> debug button click
        private void textBoxDebug_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                buttonDebug_Click((object)sender, (EventArgs)e);
        }

        //worker thread
        //-------------

        private void backgroundWorkerBbox_DoWork(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;
            try
            {
                // Get line data
                _session.GetLineData();

                // Get device info
                DeviceInfo deviceInfo = _session.GetDeviceInfo();
                setLabelText(labelHardwareVersion, deviceInfo.HardwareVersion);
                setLabelText(labelSoftwareVersion, deviceInfo.FirmwareVersion);
                setLabelText(labelGUIVersion, deviceInfo.GuiVersion);
                setLabelText(labelDeviceUptime, deviceInfo.DeviceUptime);
                setLabelText(labelLinkUptime, deviceInfo.LinkUptime);

                // Get dsl standard
                setLabelText(labelDSLStandard, _session.DSLStandard.ToString().Replace("plus", "+"));

                // Get sync values
                setLabelText(labelDownstreamCurrentBitRate, "busy...");
                setLabelText(labelDownstreamCurrentBitRate, _session.DownstreamCurrentBitRate < 0 ? "unknown" : _session.DownstreamCurrentBitRate.ToString("###,###,##0 'kbps'"));

                setLabelText(labelUpstreamCurrentBitRate, "busy...");
                setLabelText(labelUpstreamCurrentBitRate, _session.UpstreamCurrentBitRate < 0 ? "unknown" : _session.UpstreamCurrentBitRate.ToString("###,###,##0 'kbps'"));

                //distance
                setLabelText(labelDistance, "busy...");
                setLabelText(labelDistance, (_session.Distance == null ? "unknown" : ((decimal)_session.Distance).ToString("0 'm'")));

                // Get profile info
                if (_session.DSLStandard == DSLStandard.VDSL2)
                {
                    //TODO check why this is incorrect
                    //_session.getVectoringEnabled();
                    //setLabelText(labelVectoring, _session.VectoringEnabled ? "Yes" : "No");

                    ProximusLineProfile currentProfile = getProfile(_session.UpstreamCurrentBitRate, _session.DownstreamCurrentBitRate, _session.Vectoring, _session.Distance);
                    if (currentProfile == null)
                    {
                        setLabelText(labelVectoring, "unknown");
                        setLabelText(labelDLM, "unknown");
                        setLabelText(labelRepair, "unknown");
                        setLabelText(labelProximusProfile, "unknown");
                        setLabelText(labelVDSLProfile, "unknown");
                    }
                    else
                    {
                        //get vectoring status fallback: get from profile list
                        setLabelText(labelVectoring, currentProfile.VectoringEnabled ? "Yes" : "No");
                        setLabelText(labelDLM, currentProfile.DlmProfile ? "Yes" : "No");
                        setLabelText(labelRepair, currentProfile.RepairProfile ? "Yes" : "No");
                        setLabelText(labelProximusProfile, currentProfile.Name.ToString());
                        setLabelText(labelVDSLProfile, currentProfile.ProfileVDSL2.ToString().Replace("p", ""));
                    }
                }
                else
                {
                    setLabelText(labelVDSLProfile, "n/a");
                    labelVDSLProfile.ForeColor = Color.Gray;
                    vdslProfileLabel.ForeColor = Color.Gray;
                    setLabelText(labelVectoring, "n/a");
                    labelVectoring.ForeColor = Color.Gray;
                    vectoringLabel.ForeColor = Color.Gray;
                    setLabelText(labelRepair, "n/a");
                    labelRepair.ForeColor = Color.Gray;
                    repairLabel.ForeColor = Color.Gray;
                    setLabelText(labelDLM, "n/a");
                    labelDLM.ForeColor = Color.Gray;
                    dlmLabel.ForeColor = Color.Gray;
                    setLabelText(labelProximusProfile, "n/a");
                    labelProximusProfile.ForeColor = Color.Gray;
                    proximusProfileLabel.ForeColor = Color.Gray;
                }

                //get line stats
                setLabelText(labelDownstreamAttenuation, "busy...");
                setLabelText(labelDownstreamAttenuation, _session.DownstreamAttenuation < 0 ? "unknown" : _session.DownstreamAttenuation.ToString("0.0 'dB'"));

                //upstream attenuation: BBOX3 adsl only
                if (_session is Bbox3Session && new List<DSLStandard> { DSLStandard.ADSL, DSLStandard.ADSL2, DSLStandard.ADSL2plus }.Contains(_session.DSLStandard))
                {
                    setLabelText(labelUpstreamAttenuation, "busy...");
                    setLabelText(labelUpstreamAttenuation, _session.UpstreamAttenuation < 0 ? "unknown" : _session.UpstreamAttenuation.ToString("0.0 'dB'"));
                }
                else
                {
                    setLabelText(labelUpstreamAttenuation, "n/a");
                    labelUpstreamAttenuation.ForeColor = Color.Gray;
                    upstreamAttenuationLabel.ForeColor = Color.Gray;
                }

                //downstream attenuation
                setLabelText(labelDownstreamNoiseMargin, "busy...");
                setLabelText(labelDownstreamNoiseMargin, _session.DownstreamNoiseMargin < 0 ? "unknown" : _session.DownstreamNoiseMargin.ToString("0.0 'dB'"));
                
                //upstream noise margin: not for BBOX2
                if (_session is Bbox3Session || _session is FritzBoxSession)
                {
                    setLabelText(labelUpstreamNoiseMargin, "busy...");
                    setLabelText(labelUpstreamNoiseMargin, _session.UpstreamNoiseMargin < 0 ? "unknown" : _session.UpstreamNoiseMargin.ToString("0.0 'dB'")); 
                }
                else
                {
                    setLabelText(labelUpstreamNoiseMargin, "n/a");
                    labelUpstreamNoiseMargin.ForeColor = Color.Gray;
                    upstreamNoiseMarginLabel.ForeColor = Color.Gray;
                }
                
                //downstream max bitrate
                setLabelText(labelDownstreamMaxBitRate, "busy...");
                setLabelText(labelDownstreamMaxBitRate, _session.DownstreamMaxBitRate < 0 ? "unknown" : _session.DownstreamMaxBitRate.ToString("###,###,##0 'kbps'"));
                
                //upstream max bit rate: not for BBOX2
                if (_session is Bbox3Session || _session is FritzBoxSession)
                {
                    setLabelText(labelUpstreamMaxBitRate, "busy...");
                    setLabelText(labelUpstreamMaxBitRate, _session.UpstreamMaxBitRate < 0 ? "unknown" : _session.UpstreamMaxBitRate.ToString("###,###,##0 'kbps'"));
                }
                else
                {
                    setLabelText(labelUpstreamMaxBitRate, "n/a");
                    labelUpstreamMaxBitRate.ForeColor = Color.Gray;
                    upstreamMaxBitRateLabel.ForeColor = Color.Gray;
                }
            }
            catch (ThreadCancelledException)
            {

            }
            catch (Exception ex)
            {
                MessageBox.Show("Unexpected error occurred. Debug info: " + ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _session.CloseSession();
            }

            //worker.ReportProgress(100);
        }

        private void backgroundWorkerBbox_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            buttonClipboard.Enabled = true;
            buttonConnect.Enabled = true;
            buttonCancel.Enabled = false;
        }

        //util funtions
        //-------------

        private static void setLabelText(Label label, string text)
        {
            label.Invoke((MethodInvoker)delegate
            {
                label.Text = text;
            });
        }

        private Modem detectDevice()
        {
            Uri host = new Uri("http://" + textBoxIpAddress.Text);
            Uri uriBbox3S = new Uri(host, Path.Combine("cgi", "json-req"));
            Uri uriBbox3T = new Uri(host, "login.lua");
            Uri uriBbox3T2 = new Uri(host, "login.lp");
            Uri uriBbox2 = new Uri(host, "index.cgi");

            if (detectDeviceGetStatusCode(uriBbox3S) == 200)
                return Modem.BBOX3S;
            else if (detectDeviceGetStatusCode(uriBbox3T) == 200 || detectDeviceGetStatusCode(uriBbox3T2) == 200)
                return Modem.BBOX3T;
            else if (detectDeviceGetStatusCode(uriBbox2) == 200)
                return Modem.BBOX2;
            else
                return Modem.unknown;
        }

        private int detectDeviceGetStatusCode(Uri url)
        {
            int status = 0;
            try
            {
                HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
                request.AllowAutoRedirect = false;
                HttpWebResponse response = request.GetResponse() as HttpWebResponse;
                status = (int) response.StatusCode;
                response.Close();
            }
            catch (WebException ex) 
            {
                status = (int) ((HttpWebResponse)ex.Response).StatusCode;
                if (status == 400 && url.ToString().EndsWith("json-req")) //bad request bbox3/s
                    status = 200;
            }
            catch (Exception) 
            {
                status = 0;
            }
            return status;
        }


        //profiles
        //--------

        private List<ProximusLineProfile> loadEmbeddedProfiles()
        {
            //load xml doc
            XmlDocument profilesDoc = new XmlDocument();
            using (Stream stream = typeof(Form1).Assembly.GetManifestResourceStream("BBox3Tool.profile.profiles.xml"))
            {
                using (StreamReader sr = new StreamReader(stream))
                {
                    profilesDoc.LoadXml(sr.ReadToEnd());
                }
            }

            //run trough all xml profiles
            return loadProfilesFromXML(profilesDoc);
        }

        private List<ProximusLineProfile> loadProfilesFromXML(XmlDocument xmlDoc)
        {
            //run trough all xml profiles
            List<ProximusLineProfile> listProfiles = new List<ProximusLineProfile>();
            foreach (XmlNode profileNode in xmlDoc.SelectNodes("//document/profiles/profile"))
            {
                List<int> confirmedDownloadList = new List<int>();
                List<int> confirmedUploadList = new List<int>();
                foreach (XmlNode confirmedNode in profileNode.SelectNodes("confirmed"))
                {
                    confirmedDownloadList.Add(Convert.ToInt32(confirmedNode.Attributes["down"].Value));
                    confirmedUploadList.Add(Convert.ToInt32(confirmedNode.Attributes["up"].Value));
                }
                confirmedDownloadList.Add(Convert.ToInt32(profileNode.SelectNodes("official")[0].Attributes["down"].Value));
                confirmedUploadList.Add(Convert.ToInt32(profileNode.SelectNodes("official")[0].Attributes["up"].Value));

                ProximusLineProfile profile = new ProximusLineProfile(
                    profileNode.Attributes["name"].Value,
                    confirmedDownloadList.Last(),
                    confirmedUploadList.Last(),
                    Convert.ToBoolean(profileNode.Attributes["provisioning"].Value),
                    Convert.ToBoolean(profileNode.Attributes["dlm"].Value),
                    Convert.ToBoolean(profileNode.Attributes["repair"].Value),
                    Convert.ToBoolean(profileNode.Attributes["vectoring"].Value),
                    (VDSL2Profile)Enum.Parse(typeof(VDSL2Profile), "p" + profileNode.Attributes["vdsl2"].Value),
                    confirmedDownloadList.Distinct().ToList(),
                    confirmedUploadList.Distinct().ToList(),
                    Convert.ToDecimal(profileNode.Attributes["min"].Value, CultureInfo.InvariantCulture),
                    Convert.ToDecimal(profileNode.Attributes["max"].Value, CultureInfo.InvariantCulture));

                listProfiles.Add(profile);
            }
            return listProfiles;
        }

        private ProximusLineProfile getProfile(int uploadSpeed, int downloadSpeed, bool? vectoringEnabled, decimal? distance)
        {
            ProximusLineProfile profile = new ProximusLineProfile();

            lock (_profiles)
            {
                //check if speed matches with confirmed speeds
                /*List<ProximusLineProfile> confirmedMatches = _profiles.Where(x => x.ConfirmedDownloadSpeeds.Contains(downloadSpeed) && x.ConfirmedUploadSpeeds.Contains(uploadSpeed)).ToList();

                //if vectoringstatus could be determined, filter on vectoring
                if (vectoringEnabled != null)
                    confirmedMatches = confirmedMatches.Where(x => x.VectoringEnabled == vectoringEnabled).ToList();

                //1 match found
                if (confirmedMatches.Count == 1)
                    return confirmedMatches.First();

                //multiple matches found
                if (confirmedMatches.Count > 1)
                {
                    //get profile with closest distance
                    if (distance != null)
                    {
                        return confirmedMatches
                          .Select(x => new { x, diffDistance = (distance - x.DistanceMin), diffSpeed = Math.Abs(x.DownloadSpeed - downloadSpeed) })
                          .OrderBy(p => p.diffDistance < 0)
                          .ThenBy(p => p.diffDistance)
                          .ThenBy(p => p.diffSpeed)
                          .First().x;
                    }
                    //get profile with closest official download speed
                    else
                        return confirmedMatches.Select(x => new { x, diff = Math.Abs(x.DownloadSpeed - downloadSpeed) })
                          .OrderBy(p => p.diff)
                          .First().x;
                }*/

                //no matches found, get profile with closest speeds in range of +256kb
                List<ProximusLineProfile> rangeMatches = _profiles.Select(x => new { x, diffDownload = Math.Abs(x.DownloadSpeed - downloadSpeed), diffUpload = Math.Abs(x.UploadSpeed - uploadSpeed) })
                    .Where(x => x.diffDownload <= 256 && x.diffUpload <= 256)
                    .OrderBy(p => p.diffDownload)
                    .ThenBy(p => p.diffUpload)
                    .Select(y => y.x)
                    .ToList();

                //check on vectoring
                if (vectoringEnabled != null)
                    rangeMatches = rangeMatches
                        .Where(x => x.VectoringEnabled == vectoringEnabled).ToList();
                
                //check on distance
                if (distance != null)
                    rangeMatches = rangeMatches
                        .Select(x => new { x, diffDistance = (distance - x.DistanceMin), diffSpeed = Math.Abs(x.DownloadSpeed - downloadSpeed) })
                        .OrderBy(p => p.diffDistance < 0)
                        .ThenBy(p => p.diffDistance)
                        .Select(y => y.x)
                        .ToList();

                //check matches found
                if (rangeMatches.Count > 0)
                    return rangeMatches.First();
            }

            //no matches found
            return null;
        }

        //save & load settings
        //--------------------

        private void saveSettings(string username, string password, string host)
        {
            //load xml doc
            XmlDocument settingsDoc = new XmlDocument();
            using (Stream stream = typeof(Form1).Assembly.GetManifestResourceStream("BBox3Tool.settings.xml"))
            {
                using (StreamReader sr = new StreamReader(stream))
                {
                    settingsDoc.LoadXml(sr.ReadToEnd());
                }
            }
            settingsDoc.SelectSingleNode("//document/login/ip").InnerText = host;
            settingsDoc.SelectSingleNode("//document/login/user").InnerText = username;
            
            try
            {
                settingsDoc.SelectSingleNode("//document/login/password").InnerText = Crypto.EncryptStringAES(password, NetworkInterface.GetAllNetworkInterfaces().First().GetPhysicalAddress().ToString());
            }
            catch { }

            switch (_selectedModem)
            {
                case Modem.BBOX3S:
                    settingsDoc.SelectSingleNode("//document/login/device").InnerText = "BBOX3S";
                    break;
                case Modem.BBOX2:
                    settingsDoc.SelectSingleNode("//document/login/device").InnerText = "BBOX2";
                    break;
                case Modem.FritzBox7390:
                    settingsDoc.SelectSingleNode("//document/login/device").InnerText = "FRITZBOX";
                    break;
                case Modem.unknown:
                case Modem.BBOX3T:
                default:
                    break;
            }
            settingsDoc.Save("BBox3Tool.settings.xml");
        }

        private bool loadSettings()
        {
            //load xml doc
            try
            {
                if (File.Exists("BBox3Tool.settings.xml"))
                {
                    XmlDocument settingsDoc = new XmlDocument();
                    settingsDoc.Load("BBox3Tool.settings.xml");

                    //only support settings v1.0
                    if (settingsDoc.SelectSingleNode("//document/version").InnerText != "1.0")
                        return false;

                    textBoxIpAddress.Text = settingsDoc.SelectSingleNode("//document/login/ip").InnerText;
                    textBoxUsername.Text = settingsDoc.SelectSingleNode("//document/login/user").InnerText;
                    try
                    {
                        textBoxPassword.Text = Crypto.DecryptStringAES(settingsDoc.SelectSingleNode("//document/login/password").InnerText, NetworkInterface.GetAllNetworkInterfaces().First().GetPhysicalAddress().ToString());
                    }
                    catch { }
                    switch (settingsDoc.SelectSingleNode("//document/login/device").InnerText)
                    {
                        case "BBOX3S":
                            _selectedModem = Modem.BBOX3S;
                            break;
                        case "BBOX2":
                            _selectedModem = Modem.BBOX2;
                            break;
                        case "FRITZBOX":
                            _selectedModem = Modem.FritzBox7390;
                            break;
                        default:
                            _selectedModem = Modem.unknown;
                            break;
                    }
                    return true;
                }
            }
            catch
            { }
            return false;
        }

        private void deleteSettings() 
        {
            if (File.Exists("BBox3Tool.settings.xml"))
            {
                try {
                    File.Delete("BBox3Tool.settings.xml");
                }
                catch { }
            }
        }

        //live update
        //-----------

        private void backgroundWorkerLiveUpdate_DoWork(object sender, DoWorkEventArgs e)
        {
            //disable connect button until
            buttonConnect.Enabled = false;
            try
            {
                //check latest profiles & distance
                string latest = Bbox3Utils.sendRequest(_liveUpdateCheck);
                if (!string.IsNullOrEmpty(latest))
                {
                    XmlDocument latestDoc = new XmlDocument();
                    latestDoc.LoadXml(latest);
                    if (Decimal.Parse(latestDoc.SelectSingleNode("//document/version").InnerText, CultureInfo.InvariantCulture) == 1)
                    {
                        decimal latestOnlineProfile = Decimal.Parse(latestDoc.SelectSingleNode("//document/latest/profiles").InnerText, CultureInfo.InvariantCulture);
                        decimal latestBBox3Distance = Decimal.Parse(latestDoc.SelectSingleNode("//document/bbox3s/distance").InnerText, CultureInfo.InvariantCulture);

                        //check if online version is more recent then embedded verion
                        XmlDocument profilesDoc = new XmlDocument();
                        using (Stream stream = typeof(Form1).Assembly.GetManifestResourceStream("BBox3Tool.profile.profiles.xml"))
                        {
                            using (StreamReader sr = new StreamReader(stream))
                            {
                                profilesDoc.LoadXml(sr.ReadToEnd());
                            }
                        }
                        decimal latestembEddedProfile = Decimal.Parse(profilesDoc.SelectSingleNode("//document/version").InnerText, CultureInfo.InvariantCulture);

                        //more recent version found, update needed
                        if (latestOnlineProfile > latestembEddedProfile)
                        {
                            bool getOnlineProfiles = false;

                            //check if we need latest profiles
                            if (!File.Exists("BBox3Tool.profiles.xml"))
                                getOnlineProfiles = true;
                            else
                            {
                                XmlDocument localDoc = new XmlDocument();
                                localDoc.Load("BBox3Tool.profiles.xml");
                                decimal latestLocalProfile = Decimal.Parse(localDoc.SelectSingleNode("//document/version").InnerText, CultureInfo.InvariantCulture);
                                if (latestOnlineProfile > latestLocalProfile)
                                    getOnlineProfiles = true;
                            }

                            //check if profiles are already stored locally
                            if (getOnlineProfiles)
                            {
                                string latestProfiles = Bbox3Utils.sendRequest(_liveUpdateProfiles);
                                if (!string.IsNullOrEmpty(latestProfiles))
                                {
                                    XmlDocument latestprofilesDoc = new XmlDocument();
                                    latestprofilesDoc.LoadXml(latestProfiles);
                                    latestprofilesDoc.Save("BBox3Tool.profiles.xml");
                                }
                            }

                            //check again, live update could have failed
                            if (File.Exists("BBox3Tool.profiles.xml"))
                            {
                                XmlDocument localDoc = new XmlDocument();
                                localDoc.Load("BBox3Tool.profiles.xml");
                                lock (_profiles)
                                {
                                    _profiles = loadProfilesFromXML(localDoc);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                backgroundWorkerLiveUpdate.ReportProgress(100);
            }
        }

        private void backgroundWorkerLiveUpdate_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            buttonConnect.Enabled = true;
        }

        //gui
        //---

        private void panelThumb_MouseEnter(object sender, EventArgs e)
        {
            Panel panel = getPanelFromThumb(sender);
            if (panel == null)
                return;

            Color color = _colorMouseOver;
            if (checkPanelSelected(panel))
                color = _colorSelected;
            panel.BackColor = color;
        }

        private void panelThumb_MouseLeave(object sender, EventArgs e)
        {
            Panel panel = getPanelFromThumb(sender);
            if (panel == null)
                return;

            Color color = Color.WhiteSmoke;
            if (checkPanelSelected(panel))
                color = _colorSelected;

            panel.BackColor = color;
        }

        private void panelThumb_Click(object sender, EventArgs e)
        {
            //reset colors
            panelBBox3S.BackColor = Color.WhiteSmoke;
            panelBBox2.BackColor = Color.WhiteSmoke;
            panelFritzBox.BackColor = Color.WhiteSmoke;
            
            //set color
            Panel panel = getPanelFromThumb(sender);
            if (panel == null)
                return;
            panel.BackColor = _colorSelected;
            
            //select modem
            if (panel == panelBBox3S)
            {
                _selectedModem = Modem.BBOX3S;
                textBoxUsername.Text = "User";
                textBoxUsername.Enabled = true;
            }
            else if (panel == panelBBox2)
            {
                _selectedModem = Modem.BBOX2;
                textBoxUsername.Text = "admin";
                textBoxUsername.Enabled = true;
            }
            else if (panel == panelFritzBox)
            {
                _selectedModem = Modem.FritzBox7390;
                textBoxUsername.Text = "N/A";
                textBoxUsername.Enabled = false;
            }
            else
            {
                textBoxUsername.Text = "";
                textBoxUsername.Enabled = true;
                _selectedModem = Modem.unknown;
            }
        }

        private Panel getPanelFromThumb(object sender)
        {
            Panel panel = null;

            if (sender is Panel)
                panel = (Panel)sender;
            else if (sender is Label)
                panel = (Panel)((Label)sender).Parent;
            else if (sender is PictureBox)
                panel = (Panel)((PictureBox)sender).Parent;

            return panel;
        }

        private bool checkPanelSelected(Panel panel)
        {
            if (panel == panelBBox3S && _selectedModem == Modem.BBOX3S)
                return true;

            if (panel == panelBBox2 && _selectedModem == Modem.BBOX2)
                return true;

            if (panel == panelFritzBox && _selectedModem == Modem.FritzBox7390)
                return true;

            return false;
        }
    }
}
