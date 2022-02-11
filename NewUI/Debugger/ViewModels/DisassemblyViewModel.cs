﻿using Avalonia;
using Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.ReactiveUI.Controls;
using Mesen.Config;
using Mesen.Debugger.Controls;
using Mesen.Debugger.Disassembly;
using Mesen.Interop;
using Mesen.ViewModels;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Linq;
using System.Text;

namespace Mesen.Debugger.ViewModels
{
	public class DisassemblyViewModel : ViewModelBase
	{
		[Reactive] public ICodeDataProvider? DataProvider { get; set; } = null;
		[Reactive] public BaseStyleProvider StyleProvider { get; set; }
		[Reactive] public int ScrollPosition { get; set; } = 0;
		[Reactive] public int MaxScrollPosition { get; private set; } = 10000;
		[Reactive] public int TopAddress { get; private set; } = 0;
		[Reactive] public CodeLineData[] Lines { get; private set; } = Array.Empty<CodeLineData>();

		[Reactive] public int? ActiveAddress { get; set; }
		[Reactive] public int SelectedRowAddress { get; set; }
		[Reactive] public int SelectionAnchor { get; set; }
		[Reactive] public int SelectionStart { get; set; }
		[Reactive] public int SelectionEnd { get; set; }

		public DebugConfig Config { get; private set; }
		public int VisibleRowCount { get; set; } = 100;
		public bool ViewerActive { get; set; }

		private int _ignoreScrollUpdates = 0;

		[Obsolete("For designer only")]
		public DisassemblyViewModel(): this(new DebugConfig(), CpuType.Snes) { }

		public DisassemblyViewModel(DebugConfig config, CpuType cpuType)
		{
			Config = config;
			StyleProvider = new BaseStyleProvider(this);

			if(Design.IsDesignMode) {
				return;
			}

			DataProvider = new CodeDataProvider(cpuType);

			this.WhenAnyValue(x => x.DataProvider).Subscribe(x => Refresh());
			this.WhenAnyValue(x => x.TopAddress).Subscribe(x => Refresh());

			int lastValue = ScrollPosition;
			this.WhenAnyValue(x => x.ScrollPosition).Subscribe(scrollPos => {
				if(!ViewerActive) {
					ScrollPosition = lastValue;
					return;
				}

				int gap = scrollPos - lastValue;
				lastValue = scrollPos;
				if(_ignoreScrollUpdates > 0) {
					return;
				}

				if(gap != 0) {
					if(Math.Abs(gap) <= 10) {
						Scroll(gap);
					} else {
						int lineCount = DataProvider?.GetLineCount() ?? 0;
						TopAddress = Math.Max(0, Math.Min(lineCount - 1, (int)((double)lineCount / MaxScrollPosition * ScrollPosition)));
					}
				}
			});
		}

		public void Scroll(int lineNumberOffset)
		{
			ICodeDataProvider? dp = DataProvider;
			if(dp == null) {
				return;
			}
			
			SetTopAddress(dp.GetRowAddress(TopAddress, lineNumberOffset));
		}

		public void ScrollToTop()
		{
			SetSelectedRow(0);
			ScrollToAddress(0, ScrollDisplayPosition.Top);
		}

		public void ScrollToBottom()
		{
			int address = (DataProvider?.GetLineCount() ?? 0) - 1;
			SetSelectedRow(address);
			ScrollToAddress((uint)address, ScrollDisplayPosition.Bottom);
		}

		public void InvalidateVisual()
		{
			//Force DisassemblyViewer to refresh
			Lines = Lines.ToArray();
		}

		public void Refresh()
		{
			ICodeDataProvider? dp = DataProvider;
			if(dp == null) {
				return;
			}

			CodeLineData[] lines = dp.GetCodeLines(TopAddress, VisibleRowCount);
			Lines = lines;

			if(lines.Length > 0 && lines[0].Address >= 0) {
				SetTopAddress(lines[0].Address);
			}
		}

		private void SetTopAddress(int address)
		{
			int lineCount = DataProvider?.GetLineCount() ?? 0;
			address = Math.Max(0, Math.Min(lineCount - 1, address));

			_ignoreScrollUpdates++;
			TopAddress = address;
			ScrollPosition = (int)(TopAddress / (double)lineCount * MaxScrollPosition);
			_ignoreScrollUpdates--;
		}

		public void SetActiveAddress(int? pc)
		{
			ActiveAddress = pc;
			if(pc != null) {
				SetSelectedRow((int)pc);
				ScrollToAddress((uint)pc, ScrollDisplayPosition.Center);
			}
		}

		public void SetSelectedRow(int address)
		{
			SelectionStart = address;
			SelectionEnd = address;
			SelectedRowAddress = address;
			SelectionAnchor = address;

			InvalidateVisual();
		}

		public void MoveCursor(int rowOffset, bool extendSelection)
		{
			ICodeDataProvider? dp = DataProvider;
			if(dp == null) {
				return;
			}

			int address = dp.GetRowAddress(SelectedRowAddress, rowOffset);
			if(extendSelection) {
				ResizeSelectionTo(address);
			} else {
				SetSelectedRow(address);
				ScrollToAddress((uint)address, rowOffset < 0 ? ScrollDisplayPosition.Top : ScrollDisplayPosition.Bottom);
			}
		}

		public void ResizeSelectionTo(int address)
		{
			if(SelectedRowAddress == address) {
				return;
			}

			bool anchorTop = SelectionAnchor == SelectionStart;
			if(anchorTop) {
				if(address < SelectionStart) {
					SelectionEnd = SelectionStart;
					SelectionStart = address;
				} else {
					SelectionEnd = address;
				}
			} else {
				if(address < SelectionEnd) {
					SelectionStart = address;
				} else {
					SelectionStart = SelectionEnd;
					SelectionEnd = address;
				}
			}

			ScrollDisplayPosition displayPos = SelectedRowAddress < address ? ScrollDisplayPosition.Bottom : ScrollDisplayPosition.Top;
			SelectedRowAddress = address;
			ScrollToAddress((uint)address, displayPos);

			InvalidateVisual();
		}

		private bool IsAddressVisible(int address)
		{
			for(int i = 1; i < VisibleRowCount - 2 && i < Lines.Length; i++) {
				if(Lines[i].Address == address) {
					return true;
				}
			}

			return false;
		}

		public void ScrollToAddress(uint pc, ScrollDisplayPosition position = ScrollDisplayPosition.Center)
		{
			if(IsAddressVisible((int)pc)) {
				//Row is already visible, don't scroll
				return;
			}

			ICodeDataProvider? dp = DataProvider;
			if(dp == null) {
				return;
			}

			switch(position) {
				case ScrollDisplayPosition.Top: TopAddress = dp.GetRowAddress((int)pc, -1); break;
				case ScrollDisplayPosition.Center: TopAddress = dp.GetRowAddress((int)pc, -VisibleRowCount / 2 + 1); break;
				case ScrollDisplayPosition.Bottom: TopAddress = dp.GetRowAddress((int)pc, -VisibleRowCount + 2); break;
			}

			if(!IsAddressVisible((int)pc)) {
				TopAddress = dp.GetRowAddress(TopAddress, TopAddress < pc ? 1 : -1);
			}
		}

		public void CopySelection()
		{
			ICodeDataProvider? dp = DataProvider;
			if(dp == null) {
				return;
			}

			bool copyAddresses = true;
			bool copyByteCode = true;
			bool copyComments = true;
			const int commentSpacingCharCount = 25;

			int addrSize = dp.CpuType.GetAddressSize();
			string addrFormat = "X" + addrSize;
			StringBuilder sb = new StringBuilder();
			int i = SelectionStart;
			while(i <= SelectionEnd) {
				CodeLineData[] data = dp.GetCodeLines(i, 5000);

				for(int j = 0; j < data.Length; j++) {
					CodeLineData lineData = data[j];
					if(lineData.Address > SelectionEnd) {
						i = lineData.Address;
						break;
					}

					string indent = "".PadLeft(lineData.Indentation / 10);

					string codeString = lineData.Text.Trim();
					if(lineData.Flags.HasFlag(LineFlags.BlockEnd) || lineData.Flags.HasFlag(LineFlags.BlockStart)) {
						codeString = "--------" + codeString + "--------";
					}

					int padding = Math.Max(commentSpacingCharCount, codeString.Length);
					if(codeString.Length == 0) {
						padding = 0;
					}

					codeString = codeString.PadRight(padding);

					string line = indent + codeString;
					if(copyByteCode) {
						line = lineData.ByteCode.PadRight(13) + line;
					}
					if(copyAddresses) {
						if(lineData.HasAddress) {
							line = lineData.Address.ToString(addrFormat) + "  " + line;
						} else {
							line = "..".PadRight(addrSize) + "  " + line;
						}
					}
					if(copyComments && !string.IsNullOrWhiteSpace(lineData.Comment)) {
						line = line + lineData.Comment;
					}
					sb.AppendLine(line);

					i = lineData.Address;
				}
			}

			Application.Current?.Clipboard?.SetTextAsync(sb.ToString());
		}
	}

	public enum ScrollDisplayPosition
	{
		Top,
		Center,
		Bottom
	}
}
