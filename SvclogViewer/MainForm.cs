﻿using Microsoft.Win32;
using SvclogViewer.Config;
using SvclogViewer.Entities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Linq;

namespace SvclogViewer
{
    public partial class MainForm : Form
    {
        private SvcIndexer indexer = new SvcIndexer();
        private StreamReader reader = null;
        private List<TraceEvent> events = new List<TraceEvent>();
        private List<TraceEvent> filteredEvents = null;
        private long lastSelectedEventOffset = -1;
        private string loadfileonstartup;

        public MainForm(string file)
        {
            loadfileonstartup = file;
            InitializeComponent();
            Text = "SvclogViewer v" + Version.GetVersion();
            Version.CheckForUpdateAsync();
            Shown += HandleMainFormShown;
            FormClosing += HandleMainFormClosing;
            DisableInput();
            lblStatus.Text = "starting...";
            lblFilename.Text = "";
            indexer.Progress += HandleIndexerProgress;
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Step = 1;
            checkAutoFormat.CheckedChanged += HandleAutoFormatCheckedChanged;
            checkAutoFormat.Checked = Configuration.Instance.AutoXmlFormatEnabled;
            checkSyntaxColoring.CheckedChanged += HandleSyntaxColoringCheckedChanged;
            checkSyntaxColoring.Checked = Configuration.Instance.UseSyntaxColoring;
            cmbSouce.Items.Add("Any");
            cmbSouce.SelectedIndex = 0;
        }

        void HandleSyntaxColoringCheckedChanged(object sender, EventArgs e)
        {
            Configuration.Instance.UseSyntaxColoring = checkSyntaxColoring.Checked;
            textBox1.EnableSyntaxColoring = checkSyntaxColoring.Checked;
        }

        void HandleAutoFormatCheckedChanged(object sender, EventArgs e)
        {
            Configuration.Instance.AutoXmlFormatEnabled = checkAutoFormat.Checked;
            if (checkAutoFormat.Checked)
            {
                FormatXML(textBox1.Text);
            }
        }

        void HandleMainFormClosing(object sender, FormClosingEventArgs e)
        {
            Configuration.Instance.Save();
        }

        void HandleIndexerProgress(object sender, ProgressEventArgs e)
        {
            int percentage = (int)((double)e.CurrentPosition / e.FileSize * 100);
            if (percentage != progressBar.Value)
            {
                progressBar.Value = percentage;
                lblStatus.Text = "loaded " + e.CurrentPosition + " of " + e.FileSize + " bytes";
                Application.DoEvents(); // dirty hack for not having to program async
            }
        }

        void HandleMainFormShown(object sender, EventArgs e)
        {
            lblStatus.Text = "startup complete";
            if (loadfileonstartup != null)
                LoadFile(loadfileonstartup);

            if (!Configuration.Instance.HasRefusedSetAsDefault)
                CheckDefault();
        }

        private void CheckDefault()
        {
            if (!DefaultAssociationHelper.IsDefaultProgram())
            {
                if (DefaultAssociationHelper.PresentMakeDefaultQuestion(this))
                {
                    Configuration.Instance.HasRefusedSetAsDefault = false;
                    DefaultAssociationHelper.MakeDefault(this);
                }
                else
                {
                    Configuration.Instance.HasRefusedSetAsDefault = true;
                }
            }
        }

        private void LoadFile(string filename)
        {
            DisableInput();

            // Close old reader
            if (reader != null)
            {
                reader.Close();
                reader.Dispose();
            }

            // Empty datagrid
            dataGridView1.Rows.Clear();

            // Open and index new file
            var filestream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
            reader = new StreamReader(filestream, Encoding.ASCII);
            events = indexer.Index(reader);

            // Fill the source box
            cmbSouce.Items.Clear();
            cmbSouce.Items.Add("Any");
            int selectedIndex = 0;
            int currentIndex = 0;
            foreach (string source in events.Select(e => e.Source).Distinct())
            {
                currentIndex++;
                cmbSouce.Items.Add(source);
                if (source == Configuration.Instance.LastSelectedSource)
                    selectedIndex = currentIndex;
            }
            cmbSouce.SelectedIndex = selectedIndex;

            // Add indexes to grid
            filteredEvents = null;
            RefreshEventList();
            EnableInput();
            lblStatus.Text = "file loaded";
            lblFilename.Text = filename;
            Text = "SvclogViewer - " + Path.GetFileName(filename);
        }

        private void AddTraceEvent(TraceEvent evt)
        {
            string selectedSource = (string)cmbSouce.Items[cmbSouce.SelectedIndex];
            if (selectedSource != "Any" && selectedSource != evt.Source)
                return;

            var row = new DataGridViewRow();
            row.Tag = evt;
            row.CreateCells(dataGridView1, evt.TimeCreated.ToString("yyyy'-'MM'-'dd HH':'mm':'ss.fffffff") + "\r\n" + evt.Method, ((evt.PositionEnd - evt.PositionStart) / 1000.0).ToString("0.0", CultureInfo.GetCultureInfo("nl-NL")));
            dataGridView1.Rows.Add(row);
            if (evt.PositionStart == lastSelectedEventOffset)
                row.Selected = true;
        }

        private void HandleFileOpen(object sender, EventArgs e)
        {
            var dialog = new OpenFileDialog();
            dialog.CheckFileExists = true;
            dialog.Filter = "Svclog (*.svclog)|*.svclog|All files (*.*)|*.*";
            dialog.Multiselect = false;
            dialog.ReadOnlyChecked = true;
            var result = dialog.ShowDialog(this);
            if (result == DialogResult.Cancel)
                return;
            LoadFile(dialog.FileName);
        }

        private void HandleAbout(object sender, EventArgs e)
        {
            MessageBox.Show("SvclogViewer v" + Version.GetVersion() + " by Martijn Stolk");
        }

        private void HandleGridSelectionChanged(object sender, EventArgs e)
        {
            if (reader == null)
                return;
            if (dataGridView1.SelectedRows.Count == 0)
                return;
            TraceEvent evt = dataGridView1.SelectedRows[0].Tag as TraceEvent;
            if (evt == null)
                return;

            //lastSelectedEventOffset = evt.PositionStart;

            long dataLength = evt.PositionEnd - evt.PositionStart;
            reader.BaseStream.Position = evt.PositionStart;
            reader.DiscardBufferedData();
            char[] chars = new char[dataLength];
            reader.ReadBlock(chars, 0, chars.Length);
            string str = new string(chars);

            if (checkAutoFormat.Checked)
                FormatXML(str);
            else
                textBox1.Text = str;
        }

        private void FormatXML(string txtToFormat)
        {
            if (string.IsNullOrWhiteSpace(txtToFormat))
                return;
            textBox1.Text = XDocument.Parse(txtToFormat).ToString();
        }

        private void HandleFormatXML(object sender, EventArgs e)
        {
            FormatXML(textBox1.Text);
        }

        private void HandleCopyToClipboard(object sender, EventArgs e)
        {
            Clipboard.SetText(textBox1.Text, TextDataFormat.Text);
        }

        private void HandleCopySoapenvToClipboard(object sender, EventArgs e)
        {
            string text = textBox1.Text;

            try
            {
                int nsPos = text.IndexOf("http://schemas.xmlsoap.org/soap/envelope/", StringComparison.InvariantCultureIgnoreCase);
                int openingPos = text.LastIndexOf('<', nsPos);
                int openingSpacePos = text.IndexOf(" ", openingPos);
                string openingStr = text.Substring(openingPos, openingSpacePos - openingPos);
                string closingStr = openingStr.Replace("<", "</");
                int closingStartPos = text.LastIndexOf(closingStr, text.Length - 1, StringComparison.InvariantCultureIgnoreCase);
                int closingEndPos = text.IndexOf(">", closingStartPos);
                text = text.Substring(openingPos, closingEndPos - openingPos + 1);
                Clipboard.SetText(text, TextDataFormat.Text);
                textBox1.Text = text;
            }
            catch (Exception)
            {
                MessageBox.Show("Error trying to extract the soap envelope or copying it to the clipboard.");
            }
        }

        private void HandleFilterClick(object sender, EventArgs e)
        {
            if (reader == null)
                return;

            if (txtFilter.Text.Length < 5)
            {
                DialogResult res = MessageBox.Show(this, "Filtering can take a long time if your search string is too small. Are you sure you want to continue?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (res == DialogResult.No)
                    return;
            }
            DisableInput();
            dataGridView1.Rows.Clear();
            List<long> occurrences = indexer.FindOccurrences(reader, txtFilter.Text);
            var results = (from evt in events
                           from o in occurrences
                           where evt.PositionStart <= o && evt.PositionEnd >= o
                           select evt);
            filteredEvents = results.Distinct().ToList();
            RefreshEventList();
            EnableInput();
            lblStatus.Text = "done filtering";
        }

        private void HandleFilterKeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyValue == 13)
            {
                HandleFilterClick(sender, e);
            }
        }

        private void HandleFilterClear(object sender, EventArgs e)
        {
            DisableInput();
            filteredEvents = null;
            RefreshEventList();
            EnableInput();
        }

        private void RefreshEventList()
        {
            dataGridView1.Rows.Clear();
            foreach (var evt in filteredEvents ?? events)
            {
                AddTraceEvent(evt);
            }
        }

        private void HandleDragEnter(object sender, DragEventArgs e)
        {
            var data = e.Data.GetData("FileNameW");
            if (data != null && data is string[])
            {
                string[] filenames = data as string[];
                if (filenames.Length == 0)
                    return;
                string filename = filenames.First();
                if (File.Exists(filename))
                    e.Effect = DragDropEffects.Copy;
            }
        }

        private void HandleDragDrop(object sender, DragEventArgs e)
        {
            var data = e.Data.GetData("FileNameW");
            if (data != null && data is string[])
            {
                string[] filenames = data as string[];
                if (filenames.Length == 0)
                    return;
                string filename = filenames.First();
                LoadFile(filename);
            }
        }

        private void EnableInput()
        {
            splitLeftRight.Enabled = true;
            dataGridView1.ScrollBars = ScrollBars.Vertical;
            DataGridViewRow selectedRow = dataGridView1.Rows.Cast<DataGridViewRow>().Where(r => r.Selected).FirstOrDefault();
            if (selectedRow != null && !selectedRow.Displayed)
            {
                if (selectedRow.Index > 2)
                    dataGridView1.FirstDisplayedScrollingRowIndex = selectedRow.Index - 2;
                else
                    dataGridView1.FirstDisplayedScrollingRowIndex = selectedRow.Index;
            }
        }

        private void DisableInput()
        {
            if (dataGridView1.SelectedRows.Count > 0)
                lastSelectedEventOffset = ((TraceEvent)dataGridView1.SelectedRows[0].Tag).PositionStart;
            splitLeftRight.Enabled = false;
            dataGridView1.ScrollBars = ScrollBars.None;
        }

        private void HandleReloadFile(object sender, EventArgs e)
        {
            if (reader != null)
            {
                var filestream = reader.BaseStream as FileStream;
                if (filestream != null)
                {
                    if (File.Exists(filestream.Name))
                    {
                        LoadFile(filestream.Name);
                    }
                    else
                    {
                        MessageBox.Show(this, "File '" + filestream.Name + "' no longer exists.", "Oops?");
                    }
                }
            }
        }

        private void HandleSourceChanged(object sender, EventArgs e)
        {
            DisableInput();
            Configuration.Instance.LastSelectedSource = (string)cmbSouce.Items[cmbSouce.SelectedIndex];
            RefreshEventList();
            EnableInput();
        }

        private void HandleClose(object sender, EventArgs e)
        {
            Close();
        }

        private void HandleSaveRequestAsClicked(object sender, EventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "xml files (*.xml)|*.xml|All files (*.*)|*.*";
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                using (Stream s = File.Open(dialog.FileName, FileMode.CreateNew))
                {
                    using (StreamWriter sw = new StreamWriter(s))
                    {
                        sw.Write(textBox1.Text);
                    }
                }
            }
        }

        private void HandleSetAsDefaultClick(object sender, EventArgs e)
        {
            if (DefaultAssociationHelper.IsDefaultProgram())
            {
                MessageBox.Show(this, "Already set as default application to handle .svclog files...");
            }
            else
            {
                CheckDefault();
            }
        }
    }
}