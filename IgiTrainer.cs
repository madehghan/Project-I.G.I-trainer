// Project I.G.I Trainer — free for everyone
// GitHub: https://github.com/madehghan/Project-I.G.I-trainer
// Features: unlimited health, unlimited ammo, give all weapons
// EN / FA bilingual UI

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class Native {
  public const uint ACCESS = 0x001FFFFF;
  [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint a, bool b, int pid);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool CloseHandle(IntPtr h);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] buf, int n, out IntPtr r);
  [DllImport("kernel32.dll", SetLastError=true)] public static extern bool WriteProcessMemory(IntPtr h, IntPtr a, byte[] buf, int n, out IntPtr w);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
  public const uint KEYEVENTF_KEYUP = 0x0002;
  public const byte VK_CONTROL = 0x11, VK_SHIFT = 0x10, VK_P = 0x50;
}

sealed class MainForm : Form {
  public const string RepoUrl = "https://github.com/madehghan/Project-I.G.I-trainer";
  const uint PtrBase = 0x0056E210;
  // Official IGI-Internal flags (soft memory, no code patch):
  const uint AddrUnlimAmmo = 0x0056E214;   // WEAPON::UNLIMITED_AMMO_SET
  const uint AddrDebugKeys = 0x0057B194;   // DEBUG::KEYS_ENABLE
  const uint AddrDebugText = 0x00A5EA75;   // DEBUG::TEXT_ENABLE
  const int OffHealth = 0x254;
  const int OffX = 0x24, OffY = 0x2C, OffZ = 0x34;
  // Full CT ammo-type pools on player.
  static readonly int[] AmmoOff = {
    0x348, // SPAS/Jack
    0x354, // Colt
    0x360, // M16A2
    0x36C, // AK47
    0x378, // MP5/Glock
    0x384, // OFFSET_1
    0x390, // OFFSET_2
    0x39C, // OFFSET_3
    0x3A8, // OFFSET_4
    0x3B4  // OFFSET_5
  };
  const int SpawnSilenceMs = 8000;

  readonly Label status, hint, linkLabel;
  readonly CheckBox chkGod, chkAmmo;
  readonly System.Windows.Forms.Timer uiTimer;
  readonly LinkLabel repoLink;
  IntPtr hProc = IntPtr.Zero;
  readonly object gate = new object();
  int attachedPid;
  uint lastPlayer, livePlayer;
  float liveHp;
  uint liveAmmo, unlimFlag;
  int writeCount, ammoWrites, giveCount;
  string gameDir, logPath;
  volatile bool godEnabled = true, ammoEnabled = true, running = true;
  volatile string phase = "Ready / آماده";
  Thread worker;
  DateTime playerSinceUtc = DateTime.MinValue;
  DateTime lastGodWriteUtc = DateTime.MinValue;
  DateTime lastAmmoWriteUtc = DateTime.MinValue;
  DateTime lastGiveUtc = DateTime.MinValue;
  bool gaveWeaponsThisPlayer;

  static string ResolveGameDir() {
    string beside = Path.GetDirectoryName(Application.ExecutablePath);
    if (!string.IsNullOrEmpty(beside) && File.Exists(Path.Combine(beside, "IGI.exe")))
      return beside;

    // Common install folders (optional helpers)
    string[] candidates = {
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Project IGI"),
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Project IGI"),
      @"C:\Games\Project IGI",
      @"D:\Games\Project IGI",
      @"E:\Games\Project IGI"
    };
    foreach (string c in candidates) {
      try {
        if (File.Exists(Path.Combine(c, "IGI.exe"))) return c;
      } catch {}
    }
    return beside ?? Environment.CurrentDirectory;
  }

  public MainForm() {
    gameDir = ResolveGameDir();
    logPath = Path.Combine(
      string.IsNullOrEmpty(Path.GetDirectoryName(Application.ExecutablePath))
        ? gameDir
        : Path.GetDirectoryName(Application.ExecutablePath),
      "IgiTrainer.log");
    try { File.WriteAllText(logPath, ""); } catch {}

    Text = "Project I.G.I Trainer | madehghan/Project-I.G.I-trainer";
    FormBorderStyle = FormBorderStyle.FixedSingle;
    MaximizeBox = false;
    MinimizeBox = true;
    ClientSize = new System.Drawing.Size(560, 360);
    StartPosition = FormStartPosition.CenterScreen;
    Font = new System.Drawing.Font("Segoe UI", 9f);
    AcceptButton = null;
    CancelButton = null;

    hint = new Label {
      AutoSize = false, Left = 12, Top = 8, Width = 530, Height = 78,
      Text =
        "EN: Unlimited ammo · Unlimited health · All weapons (Ctrl+Shift+P)\n" +
        "FA: تیر نامحدود · جان نامحدود · همه سلاح‌ها (خودکار یا دکمه)\n" +
        "Put IgiTrainer.exe next to IGI.exe  |  فایل را کنار IGI.exe بگذارید"
    };
    Controls.Add(hint);

    chkGod = new CheckBox { Text = "Unlimited Health / جان نامحدود", Left = 12, Top = 92, Width = 250, Checked = true };
    chkAmmo = new CheckBox { Text = "Unlimited Ammo / تیر نامحدود", Left = 270, Top = 92, Width = 250, Checked = true };
    chkGod.CheckedChanged += delegate { godEnabled = chkGod.Checked; };
    chkAmmo.CheckedChanged += delegate { ammoEnabled = chkAmmo.Checked; };
    Controls.Add(chkGod); Controls.Add(chkAmmo);

    status = new Label { AutoSize = false, Left = 12, Top = 125, Width = 530, Height = 55, Text = "Ready / آماده" };
    Controls.Add(status);

    Button play = new Button {
      Text = "Play / اجرا", Left = 12, Top = 195, Width = 120, Height = 36,
      Font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Bold),
      TabStop = false
    };
    play.Click += delegate { Launch(); };
    Controls.Add(play);

    Button give = new Button {
      Text = "All Weapons / همه سلاح‌ها", Left = 140, Top = 195, Width = 210, Height = 36,
      Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold),
      TabStop = false
    };
    give.Click += delegate { GiveAllWeaponsNow(true); };
    Controls.Add(give);

    Button browse = new Button {
      Text = "Find IGI.exe…", Left = 360, Top = 195, Width = 110, Height = 36, TabStop = false
    };
    browse.Click += delegate { PickGameDir(); };
    Controls.Add(browse);

    Button ex = new Button { Text = "Exit", Left = 480, Top = 195, Width = 64, Height = 36, TabStop = false };
    ex.Click += delegate { Close(); };
    Controls.Add(ex);

    repoLink = new LinkLabel {
      AutoSize = false, Left = 12, Top = 250, Width = 530, Height = 22,
      Text = "GitHub (free): " + RepoUrl,
      LinkArea = new LinkArea(15, RepoUrl.Length)
    };
    repoLink.LinkClicked += delegate {
      try { Process.Start(new ProcessStartInfo { FileName = RepoUrl, UseShellExecute = true }); } catch {}
    };
    Controls.Add(repoLink);

    linkLabel = new Label {
      AutoSize = false, Left = 12, Top = 280, Width = 530, Height = 55,
      ForeColor = System.Drawing.Color.DimGray,
      Text =
        "Single-player helper only. You must own a legal copy of Project I.G.I.\n" +
        "فقط تک‌نفره — باید نسخه قانونی بازی را داشته باشید.\n" +
        "Source / سورس: " + RepoUrl
    };
    Controls.Add(linkLabel);

    uiTimer = new System.Windows.Forms.Timer { Interval = 300 };
    uiTimer.Tick += delegate {
      if (attachedPid == 0) status.Text = "Waiting for game… / منتظر بازی…  (Play)";
      else {
        string hpTxt = float.IsNaN(liveHp) ? "NaN✓" : liveHp.ToString("0.0");
        status.Text = phase + "\nHP=" + hpTxt + " | ammoFlag=" + unlimFlag +
          " | sample=" + liveAmmo + " | give=" + giveCount;
      }
    };
    uiTimer.Start();

    worker = new Thread(WorkerLoop) { IsBackground = true };
    worker.Start();
    Log("IgiTrainer public build — " + RepoUrl);
    Shown += delegate { ActiveControl = status; };
  }

  void Log(string m) {
    try { File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + m + "\r\n"); } catch {}
  }

  void PickGameDir() {
    using (OpenFileDialog dlg = new OpenFileDialog()) {
      dlg.Title = "Select IGI.exe / فایل IGI.exe را انتخاب کنید";
      dlg.Filter = "IGI.exe|IGI.exe|Executable (*.exe)|*.exe";
      dlg.FileName = "IGI.exe";
      if (dlg.ShowDialog(this) == DialogResult.OK) {
        gameDir = Path.GetDirectoryName(dlg.FileName);
        Log("gameDir set " + gameDir);
        phase = "Game folder set / پوشه بازی تنظیم شد";
        MessageBox.Show(this,
          "OK:\n" + gameDir + "\n\nClick Play.\nدکمه Play را بزنید.",
          Text);
      }
    }
  }

  void Launch() {
    try {
      if (Process.GetProcessesByName("IGI").Length > 0) {
        Log("Play ignored — already running");
        phase = "Game already open / بازی باز است";
        return;
      }
      if (string.IsNullOrEmpty(gameDir) || !File.Exists(Path.Combine(gameDir, "IGI.exe")))
        gameDir = ResolveGameDir();
      string exe = Path.Combine(gameDir, "IGI.exe");
      if (!File.Exists(exe)) {
        MessageBox.Show(this,
          "IGI.exe not found.\nPut this trainer next to IGI.exe, or click Find IGI.exe…\n\n" +
          "IGI.exe پیدا نشد.\nترینر را کنار IGI.exe بگذارید یا Find IGI.exe را بزنید.\n\n" +
          RepoUrl,
          Text);
        Log("missing " + exe);
        return;
      }
      foreach (Process x in Process.GetProcessesByName("IGILoader")) { try { x.Kill(); } catch {} }
      Process.Start(new ProcessStartInfo {
        FileName = exe,
        WorkingDirectory = gameDir,
        Arguments = "DebugKeys DebugText",
        UseShellExecute = true
      });
      Log("launched " + exe + " DebugKeys DebugText");
      phase = "Game started / بازی اجرا شد";
    } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
  }

  void GiveAllWeaponsNow(bool fromUi) {
    try {
      Process[] ps = Process.GetProcessesByName("IGI");
      if (ps.Length == 0) {
        if (fromUi) MessageBox.Show(this,
          "Start the game first (Play), then enter a mission.\nاول Play بزنید و وارد مرحله شوید.",
          Text);
        return;
      }
      lock (gate) {
        if (!AttachUnlocked()) return;
        EnableDebugFlags();
      }
      IntPtr hwnd = ps[0].MainWindowHandle;
      if (hwnd != IntPtr.Zero) Native.SetForegroundWindow(hwnd);
      Thread.Sleep(150);
      // Built-in debug: LCtrl + LShift + P = give all weapons and ammo
      Native.keybd_event(Native.VK_CONTROL, 0, 0, UIntPtr.Zero);
      Native.keybd_event(Native.VK_SHIFT, 0, 0, UIntPtr.Zero);
      Native.keybd_event(Native.VK_P, 0, 0, UIntPtr.Zero);
      Thread.Sleep(50);
      Native.keybd_event(Native.VK_P, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
      Native.keybd_event(Native.VK_SHIFT, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
      Native.keybd_event(Native.VK_CONTROL, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
      giveCount++;
      lastGiveUtc = DateTime.UtcNow;
      gaveWeaponsThisPlayer = true;
      Log("sent Ctrl+Shift+P give-all");
      phase = "All weapons sent / همه سلاح‌ها ارسال شد";
      if (fromUi) status.Text = phase + " — if missing, press Ctrl+Shift+P in-game";
    } catch (Exception ex) {
      Log("give: " + ex.Message);
      if (fromUi) MessageBox.Show(this, ex.Message, Text);
    }
  }

  bool AttachUnlocked() {
    Process[] ps = Process.GetProcessesByName("IGI");
    if (ps.Length == 0) {
      if (hProc != IntPtr.Zero) { Native.CloseHandle(hProc); hProc = IntPtr.Zero; }
      attachedPid = 0; livePlayer = 0; return false;
    }
    Process target = ps[0];
    for (int i = 1; i < ps.Length; i++) {
      try { if (ps[i].StartTime > target.StartTime) target = ps[i]; } catch {}
    }
    if (hProc != IntPtr.Zero && attachedPid == target.Id) return true;
    if (hProc != IntPtr.Zero) { Native.CloseHandle(hProc); hProc = IntPtr.Zero; }
    hProc = Native.OpenProcess(Native.ACCESS, false, target.Id);
    if (hProc == IntPtr.Zero) { attachedPid = 0; Log("OpenProcess err=" + Marshal.GetLastWin32Error()); return false; }
    attachedPid = target.Id;
    lastPlayer = 0; livePlayer = 0; writeCount = ammoWrites = 0;
    playerSinceUtc = DateTime.MinValue; lastGodWriteUtc = DateTime.MinValue;
    lastAmmoWriteUtc = DateTime.MinValue; lastGiveUtc = DateTime.MinValue;
    gaveWeaponsThisPlayer = false;
    Log("attached " + attachedPid);
    return true;
  }

  bool ReadBytes(uint addr, byte[] buf) {
    IntPtr r; return Native.ReadProcessMemory(hProc, (IntPtr)addr, buf, buf.Length, out r);
  }
  bool WriteBytes(uint addr, byte[] buf) {
    IntPtr w; return Native.WriteProcessMemory(hProc, (IntPtr)addr, buf, buf.Length, out w);
  }
  bool ReadU32(uint addr, out uint v) {
    v = 0; byte[] b = new byte[4];
    if (!ReadBytes(addr, b)) return false;
    v = BitConverter.ToUInt32(b, 0); return true;
  }
  bool ReadF32(uint addr, out float v) {
    v = 0; byte[] b = new byte[4];
    if (!ReadBytes(addr, b)) return false;
    v = BitConverter.ToSingle(b, 0); return true;
  }
  bool WriteF32(uint addr, float v) { return WriteBytes(addr, BitConverter.GetBytes(v)); }
  bool WriteU32(uint addr, uint v) { return WriteBytes(addr, BitConverter.GetBytes(v)); }
  bool WriteU8(uint addr, byte v) { return WriteBytes(addr, new byte[] { v }); }

  static bool SaneFloat(float v, float lo, float hi) {
    return !float.IsNaN(v) && !float.IsInfinity(v) && v >= lo && v <= hi;
  }

  void EnableDebugFlags() {
    WriteU8(AddrDebugKeys, 1);
    WriteU8(AddrDebugText, 1);
  }

  bool ResolvePlayer(out uint player, out float hp) {
    player = 0; hp = 0;
    uint a, b, c, d;
    if (!ReadU32(PtrBase, out a) || a < 0x10000) return false;
    if (!ReadU32(a + 8, out b) || b < 0x10000) return false;
    if (!ReadU32(b + 0x7CC, out c) || c < 0x10000) return false;
    if (!ReadU32(c + 0x14, out d) || d < 0x10000 || d > 0x7FFE0000) return false;
    float x, y, z;
    if (!ReadF32(d + OffX, out x) || !ReadF32(d + OffY, out y) || !ReadF32(d + OffZ, out z)) return false;
    if (!SaneFloat(x, -50000f, 50000f) || !SaneFloat(y, -50000f, 50000f) || !SaneFloat(z, -50000f, 50000f)) return false;
    if (Math.Abs(x) < 0.001f && Math.Abs(y) < 0.001f && Math.Abs(z) < 0.001f) return false;
    if (!ReadF32(d + OffHealth, out hp)) return false;
    if (float.IsInfinity(hp) || (!float.IsNaN(hp) && (hp < 0f || hp > 500f))) return false;
    player = d;
    return true;
  }

  void WorkerLoop() {
    while (running) {
      try {
        bool doAutoGive = false;
        lock (gate) {
          if (!AttachUnlocked()) {
            livePlayer = 0; phase = "Waiting for game / منتظر بازی";
            Thread.Sleep(150);
            continue;
          }

          EnableDebugFlags();
          if (ammoEnabled) {
            WriteU32(AddrUnlimAmmo, 1u);
            ReadU32(AddrUnlimAmmo, out unlimFlag);
          }

          uint player; float hp;
          if (!ResolvePlayer(out player, out hp)) {
            livePlayer = 0; playerSinceUtc = DateTime.MinValue; gaveWeaponsThisPlayer = false;
            phase = "Menu/cutscene — flags ON / منو — فلگ‌ها روشن";
            Thread.Sleep(80);
            continue;
          }

          if (player != lastPlayer) {
            lastPlayer = player;
            playerSinceUtc = DateTime.UtcNow;
            lastGodWriteUtc = DateTime.MinValue;
            lastAmmoWriteUtc = DateTime.MinValue;
            gaveWeaponsThisPlayer = false;
            Log("player 0x" + player.ToString("X") + " hp=" + hp.ToString("0.0"));
          }

          livePlayer = player;
          liveHp = hp;

          double elapsed = 0;
          if (playerSinceUtc != DateTime.MinValue)
            elapsed = (DateTime.UtcNow - playerSinceUtc).TotalMilliseconds;
          if (elapsed < SpawnSilenceMs) {
            phase = "Safe spawn " + (int)(SpawnSilenceMs - elapsed) + "ms";
            Thread.Sleep(80);
            continue;
          }

          if (godEnabled) {
            bool need = !float.IsNaN(hp);
            bool cooled = lastGodWriteUtc == DateTime.MinValue ||
              (DateTime.UtcNow - lastGodWriteUtc).TotalSeconds >= 3.0;
            if (need && cooled) {
              WriteF32(player + OffHealth, float.NaN);
              writeCount++;
              lastGodWriteUtc = DateTime.UtcNow;
              Log("unlimited health");
            }
          }

          if (ammoEnabled) {
            bool ammoCool = lastAmmoWriteUtc == DateTime.MinValue ||
              (DateTime.UtcNow - lastAmmoWriteUtc).TotalSeconds >= 2.0;
            if (ammoCool) {
              for (int i = 0; i < AmmoOff.Length; i++)
                WriteU32(player + (uint)AmmoOff[i], 999u);
              ammoWrites++;
              lastAmmoWriteUtc = DateTime.UtcNow;
            }
          }

          uint sample = 0;
          ReadU32(player + 0x360, out sample);
          liveAmmo = sample;
          ReadF32(player + OffHealth, out liveHp);

          if (!gaveWeaponsThisPlayer &&
              (lastGiveUtc == DateTime.MinValue || (DateTime.UtcNow - lastGiveUtc).TotalSeconds >= 5)) {
            doAutoGive = true;
          }

          phase = "Active / فعال | unlim=" + unlimFlag + " | DebugKeys ON";
        }

        if (doAutoGive) {
          GiveAllWeaponsNow(false);
        }

        Thread.Sleep(200);
      } catch (Exception ex) {
        Log("worker: " + ex.Message);
        Thread.Sleep(200);
      }
    }
  }

  protected override void OnFormClosed(FormClosedEventArgs e) {
    running = false;
    uiTimer.Stop();
    try { if (worker != null && worker.IsAlive) worker.Join(500); } catch {}
    lock (gate) {
      if (hProc != IntPtr.Zero) Native.CloseHandle(hProc);
      hProc = IntPtr.Zero;
    }
    base.OnFormClosed(e);
  }
}

static class Program {
  [STAThread]
  static void Main() {
    try {
      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);
      Application.Run(new MainForm());
    } catch (Exception ex) {
      try {
        File.WriteAllText(
          Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IgiTrainer.crash.txt"),
          DateTime.Now + "\r\n" + ex + "\r\n" + MainForm.RepoUrl);
      } catch {}
      try {
        MessageBox.Show(ex.Message + "\n\n" + MainForm.RepoUrl, "IgiTrainer crash");
      } catch {}
    }
  }
}
