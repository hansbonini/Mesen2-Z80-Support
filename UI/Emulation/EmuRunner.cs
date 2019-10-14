﻿using Mesen.GUI.Config;
using Mesen.GUI.Forms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mesen.GUI.Emulation
{
	public static class EmuRunner
	{
		private static Thread _emuThread = null;
		private static ResourcePath? _romPath = null;

		public static void LoadRom(ResourcePath romPath, ResourcePath? patchPath = null)
		{
			if(!frmSelectRom.SelectRom(ref romPath)) {
				return;
			}

			if(patchPath == null && ConfigManager.Config.Preferences.AutoLoadPatches) {
				string[] extensions = new string[3] { ".ips", ".ups", ".bps" };
				foreach(string ext in extensions) {
					string file = Path.Combine(romPath.Folder, Path.GetFileNameWithoutExtension(romPath.FileName)) + ext;
					if(File.Exists(file)) {
						patchPath = file;
						break;
					}
				}
			}

			_romPath = romPath;
			if(EmuApi.LoadRom(romPath, patchPath)) {
				ConfigManager.Config.RecentFiles.AddRecentFile(romPath, patchPath);
				StartEmulation();
			}
		}

		public static  void LoadPatchFile(string patchFile)
		{
			string patchFolder = Path.GetDirectoryName(patchFile);
			HashSet<string> romExtensions = new HashSet<string>() { ".sfc", ".smc", ".swc", ".fig" };
			List<string> romsInFolder = new List<string>();
			foreach(string filepath in Directory.EnumerateFiles(patchFolder)) {
				if(romExtensions.Contains(Path.GetExtension(filepath).ToLowerInvariant())) {
					romsInFolder.Add(filepath);
				}
			}

			if(romsInFolder.Count == 1) {
				//There is a single rom in the same folder as the IPS/BPS patch, use it automatically
				LoadRom(romsInFolder[0], patchFile);
			} else {
				if(!IsRunning() || !_romPath.HasValue) {
					//Prompt the user for a rom to load
					if(MesenMsgBox.Show("SelectRomIps", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) {
						using(OpenFileDialog ofd = new OpenFileDialog()) {
							ofd.SetFilter(ResourceHelper.GetMessage("FilterRom"));
							if(ConfigManager.Config.RecentFiles.Items.Count > 0) {
								ofd.InitialDirectory = ConfigManager.Config.RecentFiles.Items[0].RomFile.Folder;
							}

							if(ofd.ShowDialog(frmMain.Instance) == DialogResult.OK) {
								LoadRom(ofd.FileName, patchFile);
							}
						}
					}
				} else if(MesenMsgBox.Show("PatchAndReset", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) {
					//Confirm that the user wants to patch the current rom and reset
					LoadRom(_romPath.Value, patchFile);
				}
			}
		}

		public static void LoadRecentGame(string recentGameArchivePath)
		{
			EmuApi.LoadRecentGame(recentGameArchivePath, false /* TODO , ConfigManager.Config.Preferences.GameSelectionScreenResetGame */);
			StartEmulation();
		}

		private static bool IsPatchFile(string filename)
		{
			using(FileStream stream = File.OpenRead(filename)) {
				byte[] header = new byte[5];
				stream.Read(header, 0, 5);
				if(header[0] == 'P' && header[1] == 'A' && header[2] == 'T' && header[3] == 'C' && header[4] == 'H') {
					return true;
				} else if((header[0] == 'U' || header[0] == 'B') && header[1] == 'P' && header[2] == 'S' && header[3] == '1') {
					return true;
				}
			}
			return false;
		}

		public static void LoadFile(string filename)
		{
			if(File.Exists(filename)) {
				if(IsPatchFile(filename)) {
					LoadPatchFile(filename);
				} else if(Path.GetExtension(filename).ToLowerInvariant() == ".mss") {
					EmuApi.LoadStateFile(filename);
				} else {
					LoadRom(filename);
				}
			}
		}

		private static void StartEmulation()
		{
			_emuThread = new Thread(() => {
				EmuApi.Run();
				_emuThread = null;
			});
			_emuThread.Start();
		}

		public static bool IsRunning()
		{
			return _emuThread != null;
		}
	}
}
