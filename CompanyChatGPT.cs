using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text;

[DataContract] public class Profile {
 [DataMember] public string Id;
 [DataMember] public string Name;
 [DataMember] public string Folder;
 [DataMember] public int Color;
 [DataMember] public bool Removed;
 [DataMember] public bool Favorite;
 [DataMember] public int SortOrder;
 [DataMember] public string Logo;
 [DataMember] public string ExpectedEmail;
 [DataMember] public string ExpectedWorkspace;
}
[DataContract] public class ProfileList {
 [DataMember] public int Version=1;
 [DataMember] public List<Profile> Profiles=new List<Profile>();
 [DataMember] public string SharedStatePath;
 [DataMember] public string TestedNativeVersion;
 [DataMember] public string UpdateManifestUrl;
 [DataMember] public string PublisherThumbprint;
}
public class ProfileStore {
 public readonly string Root;
 public ProfileList State;
 string FilePath { get { return Path.Combine(Root,"profiles.json"); } }
 public ProfileStore(string root) {
  Root=Path.GetFullPath(root);
  if(File.Exists(FilePath)) {
   using(var stream=File.OpenRead(FilePath)) State=(ProfileList)new DataContractJsonSerializer(typeof(ProfileList)).ReadObject(stream);
   Validate(State);
  } else {
   State=new ProfileList();
   if(Directory.Exists(Root)) foreach(string folder in Directory.GetDirectories(Root).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase)) {
    string leaf=Path.GetFileName(folder);
    if(!System.Text.RegularExpressions.Regex.IsMatch(leaf,"^Company[1-9][0-9]*$")) continue;
    State.Profiles.Add(new Profile { Id="legacy-"+leaf.Substring(7), Name="Company "+leaf.Substring(7), Folder=leaf, Color=leaf=="Company1" ? 0 : leaf=="Company2" ? 1 : leaf=="Company3" ? 2 : State.Profiles.Count%7 });
   }
   for(int i=1;i<=3;i++) if(!State.Profiles.Any(x=>x.Folder=="Company"+i)) State.Profiles.Add(new Profile { Id="legacy-"+i,Name="Company "+i,Folder="Company"+i,Color=i-1 });
   State.Profiles=State.Profiles.OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ToList();
  }
  if(String.IsNullOrWhiteSpace(State.SharedStatePath)) {
   string normal=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","NativeCompanyProfiles");
   State.SharedStatePath=String.Equals(Root,Path.GetFullPath(normal),StringComparison.OrdinalIgnoreCase)
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex") : Path.Combine(Root,"SharedState");
  }
  State.SharedStatePath=Path.GetFullPath(State.SharedStatePath);
 }
 static void Validate(ProfileList list) {
  if(list==null || list.Version!=1 || list.Profiles==null) throw new Exception("This profile list cannot be read. Your app data has not been changed.");
  var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase); var folders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var p in list.Profiles) if(p==null || !System.Text.RegularExpressions.Regex.IsMatch(p.Id??"","^(legacy-[1-9][0-9]*|[a-f0-9]{32})$") || String.IsNullOrWhiteSpace(p.Name) || !ids.Add(p.Id) ||
   !System.Text.RegularExpressions.Regex.IsMatch(p.Folder??"","^(Company[1-9][0-9]*|profile-[a-f0-9]{32})$") || !folders.Add(p.Folder)) throw new Exception("Invalid profile entry. Your app data has not been changed.");
 }
 static ProfileList Clone(ProfileList value) {
  using(var stream=new MemoryStream()) { var s=new DataContractJsonSerializer(typeof(ProfileList)); s.WriteObject(stream,value);stream.Position=0;return (ProfileList)s.ReadObject(stream); }
 }
 void Commit(ProfileList next) {
  Validate(next);Directory.CreateDirectory(Root);string temp=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
  using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {new DataContractJsonSerializer(typeof(ProfileList)).WriteObject(stream,next);stream.Flush(true);}
  for(int attempt=0;;attempt++)try {
   if(File.Exists(FilePath))File.Replace(temp,FilePath,FilePath+".bak");else File.Move(temp,FilePath);break;
  }catch(IOException){if(attempt>=7)throw;Thread.Sleep(50*(attempt+1));}
  State=next;
 }
 string CheckName(string name,string exceptId) {
  name=(name??"").Trim();if(name.Length==0 || name.Length>60 || name.Any(Char.IsControl)) throw new Exception("Use a profile name between 1 and 60 characters.");
  if(State.Profiles.Any(x=>!x.Removed && x.Id!=exceptId && String.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase))) throw new Exception("That profile name is already in use.");return name;
 }
 public Profile Add(string name) {
  name=CheckName(name,null);var next=Clone(State);string id=Guid.NewGuid().ToString("N");
  var p=new Profile {Id=id,Name=name,Folder="profile-"+id,Color=next.Profiles.Count%7};next.Profiles.Add(p);Commit(next);return p;
 }
 public void Rename(string id,string name) {name=CheckName(name,id);var next=Clone(State);next.Profiles.Single(x=>x.Id==id).Name=name;Commit(next);}
 public void Remove(string id) {var next=Clone(State);next.Profiles.Single(x=>x.Id==id).Removed=true;Commit(next);}
 public void Restore(string id) {var p=State.Profiles.Single(x=>x.Id==id);CheckName(p.Name,id);var next=Clone(State);next.Profiles.Single(x=>x.Id==id).Removed=false;Commit(next);}
 public void Edit(string id,Action<Profile> edit) {var next=Clone(State);edit(next.Profiles.Single(x=>x.Id==id));Commit(next);}
 public void Settings(Action<ProfileList> edit) {var next=Clone(State);edit(next);Commit(next);}
 public List<Profile> Ordered() {return State.Profiles.Where(x=>!x.Removed).OrderByDescending(x=>x.Favorite).ThenBy(x=>x.SortOrder).ThenBy(x=>State.Profiles.IndexOf(x)).ToList();}
 public void Move(string source,string target) {
  if(source==target)return;var next=Clone(State);var list=Ordered();var item=list.Single(x=>x.Id==source);list.Remove(item);int index=list.FindIndex(x=>x.Id==target);if(index<0)return;list.Insert(index,item);
  bool favorite=list.Single(x=>x.Id==target).Favorite;next.Profiles.Single(x=>x.Id==source).Favorite=favorite;
  for(int i=0;i<list.Count;i++)next.Profiles.Single(x=>x.Id==list[i].Id).SortOrder=i+1;Commit(next);
 }
}
public class AppProcess { public int Id;public string ProfileId; }
public class NativeProcessRow {public int Id,ParentId;public string Name,CommandLine;}
// Native SQLite relocation shares local projects/history; auth stays in each
// profile's CODEX_HOME. No auth snapshots, token copying or owner rewriting.
public static class SharedState {
 const int Ok=0,Row=100,Done=101;
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open_v2(byte[] file,out IntPtr db,int flags,IntPtr vfs);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr db,int ms);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int n,out IntPtr statement,IntPtr tail);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr statement);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern long sqlite3_column_int64(IntPtr statement,int column);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr statement);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_backup_init(IntPtr destination,byte[] destinationName,IntPtr source,byte[] sourceName);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_backup_step(IntPtr backup,int pages);
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_backup_finish(IntPtr backup);
 static byte[] Utf8(string s) {return Encoding.UTF8.GetBytes(s+"\0");}
 static IntPtr Open(string path,bool write) {
  IntPtr db;int result=sqlite3_open_v2(Utf8(path),out db,write ? 6 : 1,IntPtr.Zero);
  if(result!=Ok) {if(db!=IntPtr.Zero)sqlite3_close(db);throw new Exception("Could not open the local chat database. No accounts were switched.");}
  sqlite3_busy_timeout(db,3000);return db;
 }
 static long Scalar(IntPtr db,string sql) {
  IntPtr statement=IntPtr.Zero;
  try {if(sqlite3_prepare_v2(db,Utf8(sql),-1,out statement,IntPtr.Zero)!=Ok || sqlite3_step(statement)!=Row)throw new Exception("Could not inspect local chat history. No accounts were switched.");return sqlite3_column_int64(statement,0);}
  finally {if(statement!=IntPtr.Zero)sqlite3_finalize(statement);}
 }
 public static long LocalItems(string home) {
  if(!Directory.Exists(home))return 0;
  long total=0;
  foreach(string path in Directory.GetFiles(home,"state_*.sqlite")) {
   IntPtr db=Open(path,false);
   try {foreach(string table in new[]{"threads","projects"})if(Scalar(db,"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='"+table+"'")>0)total+=Scalar(db,"SELECT COUNT(*) FROM "+table);}
   finally {sqlite3_close(db);}
  }
  return total;
 }
 public static long Count(string home,string table) {
  if(table!="threads" && table!="projects")throw new ArgumentException("Invalid count");long total=0;
  if(!Directory.Exists(home))return 0;
  string path=CurrentDatabase(home);if(path==null)return 0;IntPtr db=Open(path,false);try{if(Scalar(db,"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='"+table+"'")>0)total=Scalar(db,"SELECT COUNT(*) FROM "+table);}finally{sqlite3_close(db);}
  return total;
 }
 public static string CurrentDatabase(string home){if(!Directory.Exists(home))return null;return Directory.GetFiles(home,"state_*.sqlite").Where(x=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(x),"^state_[0-9]+\\.sqlite$")).OrderByDescending(x=>Int32.Parse(Path.GetFileNameWithoutExtension(x).Substring(6))).FirstOrDefault();}
 public static List<string> SampleIds(string home,string table,int limit){if(table!="threads"&&table!="projects")throw new ArgumentException("Invalid table");var ids=new List<string>();string path=CurrentDatabase(home);if(path==null)return ids;IntPtr db=Open(path,false),stmt=IntPtr.Zero;
  try{string filter="";if(table=="threads"){var columns=new HashSet<string>();IntPtr meta=IntPtr.Zero;try{if(sqlite3_prepare_v2(db,Utf8("PRAGMA table_info(threads)"),-1,out meta,IntPtr.Zero)!=Ok)throw new Exception("Could not inspect thread columns.");while(sqlite3_step(meta)==Row)columns.Add(Marshal.PtrToStringAnsi(sqlite3_column_text(meta,1)));}finally{if(meta!=IntPtr.Zero)sqlite3_finalize(meta);}var terms=new List<string>();if(columns.Contains("has_user_event"))terms.Add("has_user_event=1");if(columns.Contains("archived"))terms.Add("archived=0");if(columns.Contains("attached_thread_parent_id"))terms.Add("attached_thread_parent_id IS NULL");if(terms.Count>0)filter=" WHERE "+String.Join(" AND ",terms);}
   if(sqlite3_prepare_v2(db,Utf8("SELECT id FROM "+table+filter+" LIMIT "+Math.Max(1,Math.Min(limit,10000))),-1,out stmt,IntPtr.Zero)!=Ok)throw new Exception("Could not inspect history identifiers.");while(sqlite3_step(stmt)==Row)ids.Add(Marshal.PtrToStringAnsi(sqlite3_column_text(stmt,0)));return ids;
  }finally{if(stmt!=IntPtr.Zero)sqlite3_finalize(stmt);sqlite3_close(db);}}
 public static void CheckIntegrity(string path) {
  IntPtr db=Open(path,false),stmt=IntPtr.Zero;try{if(sqlite3_prepare_v2(db,Utf8("PRAGMA quick_check"),-1,out stmt,IntPtr.Zero)!=Ok || sqlite3_step(stmt)!=Row)throw new Exception("Database integrity check failed.");
   IntPtr result=sqlite3_column_text(stmt,0);if(Marshal.PtrToStringAnsi(result)!="ok")throw new Exception("Database integrity check failed.");
  }finally{if(stmt!=IntPtr.Zero)sqlite3_finalize(stmt);sqlite3_close(db);}
 }
 [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr stmt,int column);
 public static void BackupDatabase(string source,string destination) {
  IntPtr src=Open(source,false),dst=IntPtr.Zero,backup=IntPtr.Zero;
  try {
   dst=Open(destination,true);backup=sqlite3_backup_init(dst,Utf8("main"),src,Utf8("main"));
   if(backup==IntPtr.Zero || sqlite3_backup_step(backup,-1)!=Done)throw new Exception("The history backup could not complete. Close all Codex clients before trying again.");
   int result=sqlite3_backup_finish(backup);backup=IntPtr.Zero;if(result!=Ok)throw new Exception("The history backup could not be finalized. Launch was stopped.");
  }finally {if(backup!=IntPtr.Zero)sqlite3_backup_finish(backup);if(dst!=IntPtr.Zero)sqlite3_close(dst);sqlite3_close(src);}
 }
 public static void ValidateConfig(string config) {
  if(!File.Exists(config))return;
  foreach(string line in File.ReadAllLines(config)) {
   string trimmed=line.TrimStart();if(trimmed.StartsWith("#"))continue;
   if(System.Text.RegularExpressions.Regex.IsMatch(trimmed,"^(?:sqlite_home|\"sqlite_home\"|'sqlite_home')\\s*="))
    throw new Exception("This profile has its own sqlite_home setting. Remove that setting from its config.toml so the launcher's shared history location can apply. The file was left unchanged.");
  }
 }
 public static void Prepare(ProfileStore store,Profile profile) {
  string home=Path.Combine(store.Root,profile.Folder,"codex"),config=Path.Combine(home,"config.toml");
  ValidateConfig(config);
  if(!String.Equals(Path.GetFullPath(home),Path.GetFullPath(store.State.SharedStatePath),StringComparison.OrdinalIgnoreCase) && LocalItems(home)>0)
   throw new Exception("This profile already contains separate local chats or projects. They have been preserved. Import that history before switching this profile to the shared store.");
  Directory.CreateDirectory(store.State.SharedStatePath);
  string backupRoot=Path.Combine(store.Root,"SharedHistoryBackups"),marker=Path.Combine(backupRoot,"ready.txt");
  string identity=Path.GetFullPath(store.State.SharedStatePath);
  if(File.Exists(marker) && File.ReadAllText(marker).Trim()==identity)return;
  string destination=Path.Combine(backupRoot,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
  Directory.CreateDirectory(destination);
  foreach(string path in Directory.GetFiles(identity,"*.sqlite")) {
   if(Path.GetFileName(path).StartsWith("logs_",StringComparison.OrdinalIgnoreCase))continue;
   BackupDatabase(path,Path.Combine(destination,Path.GetFileName(path)));
  }
  string global=Path.Combine(identity,".codex-global-state.json");if(File.Exists(global))File.Copy(global,Path.Combine(destination,".codex-global-state.json"),false);
  File.WriteAllText(marker,identity);
 }
}
public static class NativeApp {
 public static void Log(string message) {
  try {string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","NativeCompanyProfiles");Directory.CreateDirectory(root);File.AppendAllText(Path.Combine(root,"launcher-diagnostics.log"),DateTime.UtcNow.ToString("o")+" "+message+Environment.NewLine);}catch{}
 }
 public static string BlockerDescription(IList<AppProcess> running) {
  var ids=new HashSet<int>(running.Select(x=>x.Id));int desktop=0,backend=0;
  foreach(var process in Process.GetProcessesByName("ChatGPT"))using(process)try{if(ids.Contains(process.Id) && process.MainWindowHandle!=IntPtr.Zero)desktop++;}catch(InvalidOperationException){}
  foreach(var process in Process.GetProcessesByName("codex"))using(process)if(ids.Contains(process.Id))backend++;
  return desktop>0 ? "ChatGPT is open. Use Close app & switch, or quit it from its tray icon." : backend>0 ? "Waiting for Codex background sessions to exit. Close Codex CLI/IDE sessions if they remain." : "Waiting for ChatGPT background processes to exit. Check its tray icon.";
 }
 public static void RequestDesktopExit() {
  int requested=0;
  foreach(var process in Process.GetProcessesByName("ChatGPT"))using(process) {
   try{if(process.MainWindowHandle==IntPtr.Zero)continue;if(process.CloseMainWindow())requested++;}catch(InvalidOperationException){}
  }
  Log("Normal desktop close requested; windows="+requested+". No processes terminated.");
 }
 public static string FindApp() {
  var s=new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -Command \"(Get-AppxPackage -Name OpenAI.Codex | Sort-Object Version -Descending | Select-Object -First 1).InstallLocation\"");
  s.UseShellExecute=false;s.CreateNoWindow=true;s.RedirectStandardOutput=true;s.RedirectStandardError=true;
  using(var p=Process.Start(s)) {var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();if(!p.WaitForExit(15000)){p.Kill();throw new TimeoutException("Finding the installed native app timed out. No profile was opened.");}string location=output.GetAwaiter().GetResult().Trim();errors.GetAwaiter().GetResult();string exe=Path.Combine(location,"app","ChatGPT.exe");
   if(p.ExitCode!=0 || String.IsNullOrEmpty(location) || !File.Exists(exe)) throw new Exception("The installed ChatGPT desktop app could not be found. Install or update it, then try again.");return exe;}
 }
 public static List<AppProcess> Running(ProfileStore store) {
  var rows=new List<NativeProcessRow>();using(var search=new ManagementObjectSearcher("SELECT Name,ProcessId,ParentProcessId,CommandLine FROM Win32_Process WHERE Name='ChatGPT.exe' OR Name='codex.exe'"))using(var values=search.Get())foreach(ManagementObject item in values)using(item)rows.Add(new NativeProcessRow{Id=Convert.ToInt32(item["ProcessId"]),ParentId=Convert.ToInt32(item["ParentProcessId"]),Name=Convert.ToString(item["Name"]),CommandLine=Convert.ToString(item["CommandLine"])});
  var ids=new HashSet<int>(rows.Select(x=>x.Id));var all=Process.GetProcessesByName("ChatGPT").Concat(Process.GetProcessesByName("codex")).ToArray();try{foreach(var p in all)if(!ids.Contains(p.Id))rows.Add(new NativeProcessRow{Id=p.Id,Name=p.ProcessName+".exe",CommandLine=""});}finally{foreach(var p in all)p.Dispose();}return Classify(store,rows);
 }
 public static List<AppProcess> Classify(ProfileStore store,IList<NativeProcessRow> rows){var direct=new Dictionary<int,string>();foreach(var row in rows){var m=System.Text.RegularExpressions.Regex.Match(row.CommandLine??"","--user-data-dir=(?:\"([^\"]+)\"|([^\\s]+))",System.Text.RegularExpressions.RegexOptions.IgnoreCase);if(!m.Success)continue;string path=m.Groups[1].Success?m.Groups[1].Value:m.Groups[2].Value;var profile=store.State.Profiles.FirstOrDefault(x=>String.Equals(Path.GetFullPath(Path.Combine(store.Root,x.Folder,"desktop")),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase));if(profile!=null)direct[row.Id]=profile.Id;}
  var result=new List<AppProcess>();foreach(var row in rows){bool backend=String.Equals(row.Name,"codex.exe",StringComparison.OrdinalIgnoreCase);bool worker=(row.CommandLine??"").IndexOf("--type=",StringComparison.OrdinalIgnoreCase)>=0;if(!backend&&worker)continue;string owner=null;direct.TryGetValue(row.Id,out owner);
   if(owner==null&&!backend){var descendants=new HashSet<int>{row.Id};bool changed=true;while(changed){changed=false;foreach(var child in rows)if(descendants.Contains(child.ParentId)&&descendants.Add(child.Id))changed=true;}var owners=direct.Where(x=>descendants.Contains(x.Key)).Select(x=>x.Value).Distinct().ToArray();if(owners.Length==1)owner=owners[0];}
   result.Add(new AppProcess{Id=row.Id,ProfileId=owner});}return result;}
 public static bool CanLaunch(IList<AppProcess> running) {return running.Count==0;}
 public static void Launch(ProfileStore store,Profile p) {
  Log("Launch requested for profile "+p.Id);
  if(p.Removed) throw new Exception("Restore this profile before opening it.");string exe=FindApp();
  if(!CanLaunch(Running(store))) throw new Exception("ChatGPT is still running. Fully quit ChatGPT and close Codex CLI/IDE sessions before opening another profile.");
  var identity=AccountService.Cached(store,p);if(identity.Mismatch)throw new Exception("Saved login differs from the expected account. Verify this profile in the picker.");
  var compatibility=CompatibilityService.Check();if(!compatibility.Passed)throw new Exception(compatibility.Message);
  if(store.State.TestedNativeVersion!=compatibility.Version)throw new Exception("Open the profile picker to review the updated native app version before launching a shortcut.");
  SharedState.Prepare(store,p);
  string folder=Path.Combine(store.Root,p.Folder),home=Path.Combine(folder,"codex"),desktop=Path.Combine(folder,"desktop");Directory.CreateDirectory(home);Directory.CreateDirectory(desktop);
  string config=Path.Combine(home,"config.toml");if(!File.Exists(config)) File.WriteAllText(config,"cli_auth_credentials_store = \"file\"\r\n");
  var s=BuildStartInfo(store,p,exe);
  if(!CanLaunch(Running(store))) throw new Exception("A ChatGPT or Codex session started while preparing history. Fully quit it before switching.");
  var launched=Process.Start(s);Log("Native process started; pid="+launched.Id+"; profile="+p.Id);
 }
 public static ProcessStartInfo BuildStartInfo(ProfileStore store,Profile p,string exe) {
  string folder=Path.Combine(store.Root,p.Folder),home=Path.Combine(folder,"codex"),desktop=Path.Combine(folder,"desktop");
  var s=new ProcessStartInfo(exe) {UseShellExecute=false,WorkingDirectory=folder,Arguments="--user-data-dir=\""+desktop+"\""};var remove=new List<string>();
  foreach(DictionaryEntry entry in s.EnvironmentVariables) {string key=(string)entry.Key;
   if(key.StartsWith("CODEX_",StringComparison.OrdinalIgnoreCase) || key.StartsWith("ELECTRON_",StringComparison.OrdinalIgnoreCase) || key.Equals("OPENAI_API_KEY",StringComparison.OrdinalIgnoreCase) || key.Equals("OPENAI_ACCESS_TOKEN",StringComparison.OrdinalIgnoreCase)) remove.Add(key);}
  foreach(string key in remove)s.EnvironmentVariables.Remove(key);s.EnvironmentVariables["CODEX_HOME"]=home;s.EnvironmentVariables["CODEX_ELECTRON_USER_DATA_PATH"]=desktop;s.EnvironmentVariables["CODEX_SQLITE_HOME"]=store.State.SharedStatePath;return s;
 }
}
public static class Theme {
 public static Color Background=Color.FromArgb(29,30,32),Surface=Color.FromArgb(48,50,53),Foreground=Color.FromArgb(244,244,245),Muted=Color.FromArgb(165,169,175),Mint=Color.FromArgb(133,224,195);
 public static Color[] Avatars={Color.FromArgb(0,132,119),Color.FromArgb(76,105,169),Color.FromArgb(168,102,56),Color.FromArgb(131,91,163),Color.FromArgb(168,77,111),Color.FromArgb(57,125,143),Color.FromArgb(105,123,64)};
 public static GraphicsPath Round(Rectangle r,int radius) {int d=radius*2;var p=new GraphicsPath();p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
 public static Label Label(string text,float size,Color color,bool bold) {return new Label {Text=text,ForeColor=color,BackColor=Color.Transparent,Font=new Font("Segoe UI",size,bold ? FontStyle.Bold : FontStyle.Regular),TextAlign=ContentAlignment.MiddleCenter};}
 public static Button Button(string text) {var b=new Button {Text=text,FlatStyle=FlatStyle.Flat,BackColor=Surface,ForeColor=Foreground,Font=new Font("Segoe UI",10),Cursor=Cursors.Hand};b.FlatAppearance.BorderColor=Color.FromArgb(83,86,91);b.FlatAppearance.MouseOverBackColor=Color.FromArgb(62,65,69);return b;}
}
public class ProfileTile : Control {
 public Profile Profile;public bool AddTile,Active;bool hover;
 public event EventHandler OpenRequested,MenuRequested;
 public ProfileTile(Profile profile,bool add) {Profile=profile;AddTile=add;Size=new Size(190,207);BackColor=Theme.Background;Cursor=Cursors.Hand;TabStop=true;AccessibleRole=AccessibleRole.PushButton;AccessibleName=add ? "Add profile" : "Open "+profile.Name;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);}
 protected override void OnMouseEnter(EventArgs e) {hover=true;Invalidate();base.OnMouseEnter(e);}
 protected override void OnMouseLeave(EventArgs e) {hover=false;Invalidate();base.OnMouseLeave(e);}
 protected override void OnGotFocus(EventArgs e) {Invalidate();base.OnGotFocus(e);}
 protected override void OnLostFocus(EventArgs e) {Invalidate();base.OnLostFocus(e);}
 protected override void OnMouseUp(MouseEventArgs e) {base.OnMouseUp(e);Focus();if(e.Button==MouseButtons.Right || (!AddTile && e.X>=Width-40 && e.Y<=42)) {if(!AddTile && MenuRequested!=null)MenuRequested(this,EventArgs.Empty);}else if(e.Button==MouseButtons.Left && ClientRectangle.Contains(e.Location) && OpenRequested!=null)OpenRequested(this,EventArgs.Empty);}
 protected override bool IsInputKey(Keys key) {return key==Keys.Enter || key==Keys.Space || base.IsInputKey(key);}
 protected override void OnKeyDown(KeyEventArgs e) {if(e.KeyCode==Keys.Enter || e.KeyCode==Keys.Space) {if(OpenRequested!=null)OpenRequested(this,EventArgs.Empty);e.Handled=true;}else if((e.Shift && e.KeyCode==Keys.F10) || e.KeyCode==Keys.Apps) {if(!AddTile && MenuRequested!=null)MenuRequested(this,EventArgs.Empty);e.Handled=true;}base.OnKeyDown(e);}
 protected override void OnPaint(PaintEventArgs e) {
  var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(BackColor);
  using(var shape=Theme.Round(new Rectangle(1,1,Width-3,Height-3),12)) {using(var fill=new SolidBrush(AddTile ? Color.FromArgb(33,35,37) : hover ? Color.FromArgb(60,63,67) : Theme.Surface))g.FillPath(fill,shape);
   using(var pen=new Pen(Active ? Theme.Mint : hover || Focused ? Color.FromArgb(125,134,140) : Color.FromArgb(71,74,79),Focused ? 2:1)) {if(AddTile)pen.DashStyle=DashStyle.Dash;g.DrawPath(pen,shape);}}
  if(AddTile) {using(var pen=new Pen(Theme.Mint,2)) {g.DrawLine(pen,Width/2-14,95,Width/2+14,95);g.DrawLine(pen,Width/2,81,Width/2,109);}
   using(var f=new Font("Segoe UI",11,FontStyle.Bold))TextRenderer.DrawText(g,"Add profile",f,new Rectangle(12,142,Width-24,30),Theme.Foreground,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
   using(var f=new Font("Segoe UI",9))TextRenderer.DrawText(g,"Another account, your choice",f,new Rectangle(8,172,Width-16,22),Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.EndEllipsis);return;}
  using(var f=new Font("Segoe UI",10,FontStyle.Bold))TextRenderer.DrawText(g,Profile.Name,f,new Rectangle(12,17,Width-47,25),Theme.Foreground,TextFormatFlags.HorizontalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
  for(int y=20;y<=30;y+=5)using(var b=new SolidBrush(Theme.Muted))g.FillEllipse(b,Width-24,y,2.5f,2.5f);
  using(var b=new SolidBrush(Theme.Avatars[Math.Abs(Profile.Color%Theme.Avatars.Length)]))g.FillEllipse(b,Width/2-37,63,74,74);
  string initial=System.Globalization.StringInfo.GetNextTextElement(Profile.Name.Trim()).ToUpperInvariant();
  using(var f=new Font("Segoe UI",28))TextRenderer.DrawText(g,initial,f,new Rectangle(Width/2-38,60,76,80),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
  using(var f=new Font("Segoe UI",10))TextRenderer.DrawText(g,"ChatGPT + Codex",f,new Rectangle(10,151,Width-20,26),Theme.Foreground,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
  using(var f=new Font("Segoe UI",8))TextRenderer.DrawText(g,Active ? "ACTIVE" : "Open native app",f,new Rectangle(10,179,Width-20,18),Active ? Theme.Mint : Theme.Muted,TextFormatFlags.HorizontalCenter);
 }
}

public class ScrollTiles : Panel {
 int offset,contentHeight,dragOrigin,dragOffset;bool dragging;
 public ScrollTiles(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);}
 public int ContentHeight {get{return contentHeight;}set{contentHeight=value;ScrollOffset=offset;Invalidate();}}
 public int ScrollOffset {
  get{return offset;}
  set{int next=Math.Max(0,Math.Min(value,Math.Max(0,contentHeight-ClientSize.Height)));int delta=offset-next;offset=next;if(delta!=0)foreach(Control c in Controls)c.Top+=delta;Invalidate();}
 }
 Rectangle Thumb() {int track=Math.Max(1,Height-20);int h=Math.Min(track,Math.Max(36,track*Height/Math.Max(1,contentHeight)));int y=10+(track-h)*offset/Math.Max(1,contentHeight-Height);return new Rectangle(Width-10,y,5,h);}
 public void EnsureVisible(Control c){if(c.Top<8)ScrollOffset+=c.Top-8;else if(c.Bottom>Height-8)ScrollOffset+=c.Bottom-Height+8;}
 protected override void OnMouseWheel(MouseEventArgs e){ScrollOffset-=e.Delta/120*80;base.OnMouseWheel(e);}
 protected override void OnMouseDown(MouseEventArgs e){if(contentHeight>Height && e.X>=Width-20){var t=Thumb();if(t.Contains(e.Location)){dragging=true;dragOrigin=e.Y;dragOffset=offset;Capture=true;}else ScrollOffset+=(e.Y<t.Top ? -1:1)*Math.Max(100,Height-30);}base.OnMouseDown(e);}
 protected override void OnMouseMove(MouseEventArgs e){if(dragging){var t=Thumb();ScrollOffset=dragOffset+(e.Y-dragOrigin)*Math.Max(1,contentHeight-Height)/Math.Max(1,Height-20-t.Height);}base.OnMouseMove(e);}
 protected override void OnMouseUp(MouseEventArgs e){dragging=false;Capture=false;base.OnMouseUp(e);}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(contentHeight<=Height)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var b=new SolidBrush(Color.FromArgb(52,55,58)))e.Graphics.FillRectangle(b,Width-10,10,5,Height-20);using(var p=Theme.Round(Thumb(),2))using(var b=new SolidBrush(Color.FromArgb(113,118,122)))e.Graphics.FillPath(b,p);}
}

public class Picker : Form {
 [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 protected override void OnHandleCreated(EventArgs e) {base.OnHandleCreated(e);try {int enabled=1;if(DwmSetWindowAttribute(Handle,20,ref enabled,4)!=0)DwmSetWindowAttribute(Handle,19,ref enabled,4);}catch {}}
 readonly ProfileStore store;
 readonly ScrollTiles grid=new ScrollTiles();readonly Panel header=new Panel(),footer=new Panel();readonly TextBox search=new TextBox();
 readonly Label title,subtitle,wordmark,status,count,single;readonly Button cancel,removed;readonly PictureBox logo;
 readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();List<AppProcess> running=new List<AppProcess>();
 string pendingId;bool busy,polling,preview;
 public Picker(ProfileStore value,bool previewMode) {
  store=value;preview=previewMode;Text="OpenAI Profiles - Independent launcher";BackColor=Theme.Background;ForeColor=Theme.Foreground;Font=new Font("Segoe UI",10);ClientSize=new Size(1024,748);MinimumSize=new Size(760,640);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;DoubleBuffered=true;
  try {using(var stream=typeof(Picker).Assembly.GetManifestResourceStream("OpenAI.ico")) {if(stream!=null)Icon=new Icon(stream,32,32);}}catch {}
  header.Dock=DockStyle.Top;header.Height=244;header.BackColor=Theme.Background;footer.Dock=DockStyle.Bottom;footer.Height=100;footer.BackColor=Color.FromArgb(33,35,38);grid.Dock=DockStyle.Fill;grid.BackColor=Theme.Background;
  Controls.Add(grid);Controls.Add(header);Controls.Add(footer);
  logo=new PictureBox {Size=new Size(38,38),SizeMode=PictureBoxSizeMode.Zoom,BackColor=Theme.Background};using(var imageStream=typeof(Picker).Assembly.GetManifestResourceStream("OpenAI.logo.png")) {if(imageStream!=null)using(var image=Image.FromStream(imageStream))logo.Image=new Bitmap(image);}header.Controls.Add(logo);
  wordmark=Theme.Label("OpenAI",19,Theme.Foreground,true);header.Controls.Add(wordmark);title=Theme.Label("Who's using ChatGPT?",24,Theme.Foreground,false);header.Controls.Add(title);subtitle=Theme.Label("Same local chats and projects. Choose the account you want to use.",11,Theme.Muted,false);header.Controls.Add(subtitle);
  search.BorderStyle=BorderStyle.FixedSingle;search.BackColor=Theme.Surface;search.ForeColor=Theme.Foreground;search.Font=new Font("Segoe UI",11);search.AccessibleName="Search profiles";search.TextChanged+=delegate {Rebuild();};header.Controls.Add(search);
  var hint=Theme.Label("SEARCH PROFILES",8,Theme.Muted,true);hint.Name="searchHint";header.Controls.Add(hint);
  single=Theme.Label("\u25cf  ONE PROFILE AT A TIME",8,Theme.Mint,true);single.TextAlign=ContentAlignment.MiddleLeft;footer.Controls.Add(single);
  status=Theme.Label("Shared history is enabled. Choose an account to get started.",9,Theme.Muted,false);status.TextAlign=ContentAlignment.MiddleLeft;status.AutoEllipsis=true;footer.Controls.Add(status);count=Theme.Label("",9,Theme.Muted,false);count.TextAlign=ContentAlignment.MiddleRight;footer.Controls.Add(count);
  var credit=Theme.Label("Shared local chats & projects \u00b7 Separate account logins",8,Theme.Muted,false);credit.Name="credit";credit.TextAlign=ContentAlignment.MiddleLeft;footer.Controls.Add(credit);
  cancel=Theme.Button("Cancel switch");cancel.Visible=false;cancel.Click+=delegate {pendingId=null;cancel.Visible=false;status.Text="Switch canceled.";};footer.Controls.Add(cancel);
  removed=Theme.Button("Restore profiles");removed.FlatAppearance.BorderSize=0;removed.Font=new Font("Segoe UI",9);removed.Click+=delegate {RestoreMenu();};footer.Controls.Add(removed);
  Resize+=delegate {LayoutParts();Rebuild();};grid.Resize+=delegate {LayoutTiles();};timer.Interval=2000;timer.Tick+=async delegate {await RefreshProcesses();};
  Shown+=async delegate {LayoutParts();Rebuild();if(!preview) {await RefreshProcesses();timer.Start();}};FormClosed+=delegate {timer.Stop();timer.Dispose();if(logo.Image!=null)logo.Image.Dispose();};LayoutParts();Rebuild();
 }
 void LayoutParts() {
  int w=header.ClientSize.Width;logo.Location=new Point(w/2-77,27);wordmark.SetBounds(w/2-29,25,115,42);title.SetBounds(18,87,w-36,45);subtitle.SetBounds(18,137,w-36,31);int sw=Math.Min(360,w-100);search.SetBounds((w-sw)/2,202,sw,31);header.Controls["searchHint"].SetBounds((w-sw)/2,181,sw,19);
  int fw=footer.ClientSize.Width;single.SetBounds(35,13,360,23);count.SetBounds(fw-175,13,140,23);status.SetBounds(35,39,Math.Max(200,fw-225),24);cancel.SetBounds(fw-173,39,138,29);footer.Controls["credit"].SetBounds(35,70,Math.Max(200,fw-220),20);removed.SetBounds(fw-175,66,140,28);
 }
 void LayoutTiles() {
  int available=grid.ClientSize.Width-SystemInformation.VerticalScrollBarWidth;int columns=Math.Max(1,Math.Min(Math.Min(5,Math.Max(1,grid.Controls.Count)),(available-44)/206));int width=columns*190+(columns-1)*16;int x=Math.Max(22,(available-width)/2),i=0;grid.SuspendLayout();
  foreach(Control tile in grid.Controls) {tile.Location=new Point(x+(i%columns)*206,18+(i/columns)*223-grid.ScrollOffset);i++;}grid.ContentHeight=Math.Max(0,((i+columns-1)/columns)*223+24);grid.ResumeLayout();
 }
 void Rebuild() {
  int scroll=grid.ScrollOffset;grid.ScrollOffset=0;grid.SuspendLayout();while(grid.Controls.Count>0) {var c=grid.Controls[0];grid.Controls.RemoveAt(0);c.Dispose();}string term=search.Text.Trim();
  foreach(var p in store.State.Profiles.Where(x=>!x.Removed && (term.Length==0 || x.Name.IndexOf(term,StringComparison.CurrentCultureIgnoreCase)>=0))) {
   var tile=new ProfileTile(p,false);tile.Active=running.Any(x=>x.ProfileId==p.Id);tile.OpenRequested+=async delegate {await RequestOpen(p.Id);};tile.MenuRequested+=delegate {ProfileMenu(tile,p.Id);};tile.GotFocus+=delegate {grid.EnsureVisible(tile);};grid.Controls.Add(tile);
  }
  var add=new ProfileTile(null,true);add.OpenRequested+=delegate {AddProfile();};add.GotFocus+=delegate {grid.EnsureVisible(add);};grid.Controls.Add(add);int total=store.State.Profiles.Count(x=>!x.Removed);count.Text=total+(total==1 ? " profile" : " profiles");removed.Visible=store.State.Profiles.Any(x=>x.Removed);LayoutTiles();grid.ScrollOffset=scroll;grid.ResumeLayout();
 }
 string AskName(string caption,string initial) {
  using(var d=new Form {Text=caption,ClientSize=new Size(420,186),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,StartPosition=FormStartPosition.CenterParent,BackColor=Theme.Background,ForeColor=Theme.Foreground,Font=new Font("Segoe UI",10)}) {
   var label=Theme.Label("Profile name",11,Theme.Foreground,true);label.TextAlign=ContentAlignment.MiddleLeft;label.SetBounds(24,19,370,25);d.Controls.Add(label);var input=new TextBox {Text=initial,MaxLength=60,BackColor=Theme.Surface,ForeColor=Theme.Foreground,BorderStyle=BorderStyle.FixedSingle,Font=new Font("Segoe UI",12)};input.SetBounds(24,54,372,32);d.Controls.Add(input);
   var ok=Theme.Button(caption.StartsWith("Add") ? "Add profile" : "Save name");ok.SetBounds(262,120,134,39);ok.DialogResult=DialogResult.OK;d.Controls.Add(ok);var no=Theme.Button("Cancel");no.SetBounds(148,120,104,39);no.DialogResult=DialogResult.Cancel;d.Controls.Add(no);d.AcceptButton=ok;d.CancelButton=no;d.Shown+=delegate {input.Focus();input.SelectAll();};return d.ShowDialog(this)==DialogResult.OK ? input.Text : null;
  }
 }
 void Error(Exception ex) {MessageBox.Show(this,ex.Message,"OpenAI Profiles",MessageBoxButtons.OK,MessageBoxIcon.Information);}
 void AddProfile() {if(busy)return;string name=AskName("Add profile","");if(name==null)return;try {var p=store.Add(name);search.Clear();Rebuild();status.Text=p.Name+" added. Select its card to sign in.";}catch(Exception ex){Error(ex);}}
 void ProfileMenu(ProfileTile tile,string id) {
  var menu=new ContextMenuStrip {BackColor=Theme.Surface,ForeColor=Theme.Foreground,ShowImageMargin=false};
  menu.Items.Add("Rename",null,delegate {var p=store.State.Profiles.Single(x=>x.Id==id);string name=AskName("Rename profile",p.Name);if(name==null)return;try {store.Rename(id,name);Rebuild();}catch(Exception ex){Error(ex);}});
  menu.Items.Add("Remove",null,async delegate {if(busy)return;try {var current=await Task.Run(()=>NativeApp.Running(store));if(current.Any(x=>x.ProfileId==id))throw new Exception("Fully quit this profile before removing it.");var p=store.State.Profiles.Single(x=>x.Id==id);
   if(MessageBox.Show(this,"Remove \""+p.Name+"\" from the picker?\n\nIts saved login stays on this PC, and shared chats and projects are kept. You can restore it later. This does not sign the account out.","Remove profile",MessageBoxButtons.OKCancel,MessageBoxIcon.Question)!=DialogResult.OK)return;
   store.Remove(id);if(pendingId==id){pendingId=null;cancel.Visible=false;}Rebuild();status.Text="Profile removed. Its login is retained; shared history is unchanged.";}catch(Exception ex){Error(ex);}});
  menu.Closed+=delegate {menu.Dispose();};menu.Show(tile,new Point(tile.Width-27,37));
 }
 void RestoreMenu() {var menu=new ContextMenuStrip {BackColor=Theme.Surface,ForeColor=Theme.Foreground,ShowImageMargin=false};foreach(var p in store.State.Profiles.Where(x=>x.Removed)) {string id=p.Id;menu.Items.Add(p.Name,null,delegate {try {store.Restore(id);Rebuild();status.Text="Profile restored.";}catch(Exception ex){Error(ex);}});}menu.Closed+=delegate {menu.Dispose();};menu.Show(removed,new Point(0,-menu.PreferredSize.Height));}
 async Task RefreshProcesses() {
  if(preview || polling || busy || IsDisposed)return;polling=true;
  try {running=await Task.Run(()=>NativeApp.Running(store));if(IsDisposed)return;foreach(ProfileTile tile in grid.Controls){tile.Active=tile.Profile!=null && running.Any(x=>x.ProfileId==tile.Profile.Id);tile.Invalidate();}
   if(pendingId!=null && NativeApp.CanLaunch(running)){string id=pendingId;pendingId=null;cancel.Visible=false;await OpenNow(id);}else if(pendingId!=null)status.Text="Switch queued. Quit ChatGPT and close Codex CLI/IDE sessions to continue.";
   else if(running.Count>0){var active=store.State.Profiles.FirstOrDefault(p=>running.Any(x=>x.ProfileId==p.Id));status.Text=active==null ? "ChatGPT or Codex is running. Close it before choosing an account here." : active.Name+" is active. Quit ChatGPT and Codex CLI/IDE sessions to switch.";}
  }catch(Exception){status.Text="Could not check running ChatGPT/Codex sessions. Opening is blocked until the check succeeds.";}finally{polling=false;}
 }
 async Task RequestOpen(string id) {if(busy || preview)return;try {running=await Task.Run(()=>NativeApp.Running(store));if(running.Any(x=>x.ProfileId==id)){status.Text="This profile is already open.";return;}if(!NativeApp.CanLaunch(running)){pendingId=id;cancel.Visible=true;status.Text="Switch queued. Quit ChatGPT and close Codex CLI/IDE sessions to continue.";return;}await OpenNow(id);}catch(Exception ex){Error(ex);}}
 async Task OpenNow(string id) {if(busy)return;busy=true;grid.Enabled=false;UseWaitCursor=true;try {var p=store.State.Profiles.Single(x=>x.Id==id && !x.Removed);status.Text="Opening "+p.Name+"...";await Task.Run(()=>NativeApp.Launch(store,p));status.Text=p.Name+" launch requested. Confirm the account in the native app.";}catch(Exception ex){status.Text="Could not open the profile.";Error(ex);}finally{busy=false;grid.Enabled=true;UseWaitCursor=false;}}
}
public static class Program {
 static void Assert(bool value,string label){if(!value)throw new Exception("Check failed: "+label);}
 static void SelfTest(string directory) {
  string root=Path.GetFullPath(directory);if(Directory.Exists(root))throw new Exception("Self-test needs a new scratch directory.");var store=new ProfileStore(root);string existing=Path.Combine(root,"Company1","codex");Directory.CreateDirectory(existing);string sentinel=Path.Combine(existing,"retention-check.txt");File.WriteAllText(sentinel,"keep this local data");
  var original=store.State.Profiles[0];store.Rename(original.Id,"Research & Development");Assert(store.State.Profiles[0].Folder=="Company1","rename retains folder identity");for(int i=1;i<=125;i++)store.Add("Test profile "+i);var again=new ProfileStore(root);Assert(again.State.Profiles.Count==128,"more than three persisted profiles");again.Remove(original.Id);Assert(File.ReadAllText(sentinel)=="keep this local data","remove retains data");Assert(new ProfileStore(root).State.Profiles.Single(x=>x.Id==original.Id).Removed,"removal persists");again.Restore(original.Id);Assert(!new ProfileStore(root).State.Profiles.Single(x=>x.Id==original.Id).Removed,"restore persists");bool duplicate=false;try{again.Add("research & development");}catch{duplicate=true;}Assert(duplicate,"duplicate names rejected");Assert(NativeApp.CanLaunch(new List<AppProcess>()),"idle allows launch");Assert(!NativeApp.CanLaunch(new List<AppProcess>{new AppProcess {Id=123}}),"unknown app blocks launch");Assert(!NativeApp.CanLaunch(new List<AppProcess>{new AppProcess {Id=123,ProfileId="other"}}),"different active profile blocks launch");
  string migration=Path.Combine(root,"migration");Directory.CreateDirectory(Path.Combine(migration,"Company2","codex"));var imported=new ProfileStore(migration);Assert(imported.State.Profiles.Any(x=>x.Folder=="Company2" && x.Id=="legacy-2"),"legacy profile retained");Assert(imported.State.Profiles.Count==3,"legacy slots retained");
  File.WriteAllText(Path.Combine(root,"self-test-result.txt"),"PASS: 125 added profiles persisted; rename kept folder identity; remove and restore retained local data; duplicate protection; legacy folder migration; single-active guard.\r\n");
 }
 [STAThread] public static int Main(string[] args) {
  bool quiet=args.Length>0 && (args[0]=="--self-test" || args[0]=="--check" || args[0]=="--guard-check" || args[0]=="--preview");
  try {
   if(args.Length==1 && args[0]=="--check"){NativeApp.FindApp();return 0;}if(args.Length==2 && args[0]=="--self-test"){SelfTest(args[1]);return 0;}
   string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","NativeCompanyProfiles");int ri=Array.IndexOf(args,"--profile-root");if(ri>=0 && ri+1<args.Length)root=Path.GetFullPath(args[ri+1]);var store=new ProfileStore(root);
   if(args.Length==2 && args[0]=="--guard-check"){var active=NativeApp.Running(store);File.WriteAllText(args[1],"Native app detected: "+(active.Count>0)+"\r\nLaunch permitted: "+NativeApp.CanLaunch(active)+"\r\n");return 0;}
   using(var mutex=new Mutex(false,"Local\\CompanyChatGPT.ProfilePicker.v3")) {
    if(!mutex.WaitOne(0))throw new Exception("The profile picker is already open. Use its existing window.");
    try {
     if(args.Length>=2 && args[0]=="--launch"){int n;var choices=store.State.Profiles.Where(x=>!x.Removed).ToList();var p=Int32.TryParse(args[1],out n) ? choices.ElementAtOrDefault(n-1) : choices.FirstOrDefault(x=>x.Id==args[1]);if(p==null)throw new Exception("That profile could not be found.");NativeApp.Launch(store,p);return 0;}
     bool preview=args.Length>=2 && args[0]=="--preview";
     var app=new System.Windows.Application();var window=new PremiumPicker(store,preview);
     if(preview){if(args.Contains("--compact")){window.Width=800;window.Height=700;}window.Show();if(args.Contains("--dialog"))window.PreviewAddDialog(args[1]);else window.SavePreview(args[1]);window.Close();app.Shutdown();return 0;}
     app.Run(window);return 0;
    }finally{mutex.ReleaseMutex();}
   }
  }catch(Exception ex){if(quiet){try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"launcher-check-error.txt"),ex.ToString());}catch{}}else MessageBox.Show(ex.Message,"OpenAI Profiles",MessageBoxButtons.OK,MessageBoxIcon.Information);return 1;}
 }
}

// A captured identity prevents a recycled PID from terminating a new process.
public sealed class NativeStopTarget { public int Id; public long Started; public string Path,Name; }
public static class NativeSessionStop {
 public static HashSet<int> SelectIds(IList<NativeProcessRow> rows,IEnumerable<int> blockers){
  var ids=new HashSet<int>(blockers);bool changed=true;
  while(changed){changed=false;foreach(var row in rows)if(ids.Contains(row.ParentId)&&(String.Equals(row.Name,"ChatGPT.exe",StringComparison.OrdinalIgnoreCase)||String.Equals(row.Name,"codex.exe",StringComparison.OrdinalIgnoreCase)))if(ids.Add(row.Id))changed=true;}
  return ids;
 }
 public static List<NativeStopTarget> Capture(ProfileStore store){
  var blockers=NativeApp.Running(store);var rows=new List<NativeProcessRow>();
  using(var search=new ManagementObjectSearcher("SELECT Name,ProcessId,ParentProcessId FROM Win32_Process WHERE Name='ChatGPT.exe' OR Name='codex.exe'"))using(var values=search.Get())foreach(ManagementObject row in values)using(row)rows.Add(new NativeProcessRow{Id=Convert.ToInt32(row["ProcessId"]),ParentId=Convert.ToInt32(row["ParentProcessId"]),Name=Convert.ToString(row["Name"])});
  var result=new List<NativeStopTarget>();foreach(int id in SelectIds(rows,blockers.Select(x=>x.Id)))try{using(var process=Process.GetProcessById(id)){result.Add(new NativeStopTarget{Id=id,Started=process.StartTime.ToUniversalTime().Ticks,Path=process.MainModule.FileName,Name=process.ProcessName});}}catch(ArgumentException){}catch(InvalidOperationException){}
  return result;
 }
 public static bool Matches(Process process,NativeStopTarget target){return process.Id==target.Id&&!process.HasExited&&process.StartTime.ToUniversalTime().Ticks==target.Started&&String.Equals(process.ProcessName,target.Name,StringComparison.OrdinalIgnoreCase)&&String.Equals(process.MainModule.FileName,target.Path,StringComparison.OrdinalIgnoreCase);}
 public static void Stop(IList<NativeStopTarget> targets){
  // First give only the captured native windows a chance to close normally.
  foreach(var target in targets)try{using(var process=Process.GetProcessById(target.Id))if(Matches(process,target))process.CloseMainWindow();}catch(ArgumentException){}catch(InvalidOperationException){}
  Thread.Sleep(2000);var failures=new List<int>();
  foreach(var target in targets.OrderBy(x=>String.Equals(x.Name,"ChatGPT",StringComparison.OrdinalIgnoreCase)?1:0))try{using(var process=Process.GetProcessById(target.Id)){if(!Matches(process,target))continue;process.Kill();if(!process.WaitForExit(5000))failures.Add(target.Id);}}catch(ArgumentException){}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){failures.Add(target.Id);}
  NativeApp.Log("User-confirmed force quit completed; captured="+targets.Count+"; failed="+failures.Count);
  if(failures.Count>0)throw new Exception("Some captured native processes could not be stopped (PID "+String.Join(", ",failures)+"). The next account will stay queued until they exit.");
 }
}
