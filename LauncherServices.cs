using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.IO.Compression;
using System.Reflection;

[assembly: AssemblyVersion("5.1.0.0")]
[assembly: AssemblyFileVersion("5.1.0.0")]

public static class FeatureJson {
 public static JavaScriptSerializer Json {get{return new JavaScriptSerializer {MaxJsonLength=16*1024*1024};}}
 public static Dictionary<string,object> Obj(object value){return value as Dictionary<string,object>??new Dictionary<string,object>();}
 public static string Str(Dictionary<string,object> data,string key){object value;return data.TryGetValue(key,out value)&&value!=null?Convert.ToString(value):"";}
 public static object Get(Dictionary<string,object> data,string key){object value;return data.TryGetValue(key,out value)?value:null;}
 public static void Write(string path,object data){File.WriteAllText(path,Json.Serialize(data),new UTF8Encoding(false));}
}
public class IdentityInfo {
 public string Email="",Workspace="",State="No saved login",Plan="",CredentialStamp="";public bool Verified,Mismatch;
 public string Display {get{return String.IsNullOrEmpty(Email)?State:Email+(String.IsNullOrEmpty(Workspace)?"":" | "+Workspace)+"\n"+State;}}
}
public sealed class NativeRpc:IDisposable {
 readonly Process child;readonly BlockingCollection<Dictionary<string,object>> replies=new BlockingCollection<Dictionary<string,object>>();int counter;
 public NativeRpc(ProfileStore store,Profile p){
  string exe=Path.Combine(Path.GetDirectoryName(NativeApp.FindApp()),"resources","codex.exe");
  var start=NativeApp.BuildStartInfo(store,p,exe);start.Arguments="app-server --stdio";start.CreateNoWindow=true;start.RedirectStandardInput=true;start.RedirectStandardOutput=true;start.RedirectStandardError=true;
  Directory.CreateDirectory(start.EnvironmentVariables["CODEX_HOME"]);
  child=new Process{StartInfo=start,EnableRaisingEvents=true};child.OutputDataReceived+=(s,e)=>{if(String.IsNullOrWhiteSpace(e.Data))return;try{replies.Add(FeatureJson.Obj(FeatureJson.Json.DeserializeObject(e.Data)));}catch{}};
  child.ErrorDataReceived+=(s,e)=>{};child.Start();child.BeginOutputReadLine();child.BeginErrorReadLine();
  try {Call("initialize",new{clientInfo=new{name="independent_profile_health_check",version=ReleaseService.Version},capabilities=new{experimentalApi=true}});Send(new{method="initialized"});}catch{Dispose();throw;}
 }
 void Send(object value){child.StandardInput.WriteLine(FeatureJson.Json.Serialize(value));child.StandardInput.Flush();}
 public Dictionary<string,object> Call(string method,object parameters){int id=++counter;Send(new{id=id,method=method,@params=parameters});var watch=Stopwatch.StartNew();
  while(watch.Elapsed.TotalSeconds<25){Dictionary<string,object> message;if(!replies.TryTake(out message,500)){if(child.HasExited)throw new Exception("Native service exited before replying (exit "+child.ExitCode+"). Check app compatibility and local configuration.");continue;}object mid=FeatureJson.Get(message,"id");if(mid!=null&&message.ContainsKey("method")){Send(new{id=mid,error=new{code=-32601,message="This profile health checker does not implement interactive callbacks."}});throw new Exception("The native service needs an interactive step. Open this profile in ChatGPT to complete sign-in or permissions.");}if(mid==null||Convert.ToString(mid)!=id.ToString())continue;
   if(message.ContainsKey("error"))throw new Exception("Native request "+method+" failed (code "+FeatureJson.Str(FeatureJson.Obj(message["error"]),"code")+"). Check sign-in, compatibility and workspace access in ChatGPT.");return FeatureJson.Obj(FeatureJson.Get(message,"result"));}
  throw new TimeoutException("The native health check timed out. No account was switched.");
 }
 public void Dispose(){try{if(child!=null&&!child.HasExited){child.StandardInput.Close();if(!child.WaitForExit(1500)){child.Kill();child.WaitForExit(3000);}}}catch{}if(child!=null)child.Dispose();}
}
public static class AccountService {
 public static string Stamp(ProfileStore store,Profile p){string file=Path.Combine(store.Root,p.Folder,"codex","auth.json");try{return File.Exists(file)?BackupService.Hash(file):"none";}catch{return "unavailable";}}
 public static IdentityInfo Cached(ProfileStore store,Profile p){var result=new IdentityInfo{CredentialStamp=Stamp(store,p)};string file=Path.Combine(store.Root,p.Folder,"codex","auth.json");if(!File.Exists(file))return result;
  try{var data=FeatureJson.Obj(FeatureJson.Json.DeserializeObject(File.ReadAllText(file)));var tokens=FeatureJson.Obj(FeatureJson.Get(data,"tokens"));string token=FeatureJson.Str(tokens,"id_token");
   if(token.Length==0){result.State="Saved credentials; verify in app";return result;}var parts=token.Split('.');if(parts.Length!=3)throw new FormatException();string payload=parts[1].Replace('-','+').Replace('_','/');payload=payload.PadRight((payload.Length+3)/4*4,'=');var claims=FeatureJson.Obj(FeatureJson.Json.DeserializeObject(Encoding.UTF8.GetString(Convert.FromBase64String(payload))));var auth=FeatureJson.Obj(FeatureJson.Get(claims,"https://api.openai.com/auth"));
   result.Email=FeatureJson.Str(claims,"email");result.Workspace=FeatureJson.Str(tokens,"account_id");if(result.Workspace.Length==0)result.Workspace=FeatureJson.Str(auth,"chatgpt_account_id");result.State="Saved login - not server verified";object exp=FeatureJson.Get(claims,"exp");if(exp!=null&&Convert.ToDouble(exp)<(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds)result.State="Cached login expired; app may refresh it";
   ApplyExpected(result,p);return result;
  }catch{result.State="Saved login could not be identified";return result;}
 }
 public static void ApplyExpected(IdentityInfo identity,Profile p){identity.Mismatch=(!String.IsNullOrWhiteSpace(p.ExpectedEmail)&&!String.Equals(identity.Email,p.ExpectedEmail,StringComparison.OrdinalIgnoreCase))||(!String.IsNullOrWhiteSpace(p.ExpectedWorkspace)&&!String.Equals(identity.Workspace,p.ExpectedWorkspace,StringComparison.Ordinal));if(identity.Mismatch)identity.State="Identity does not match this profile's expected account";}
 public static IdentityInfo Verify(ProfileStore store,Profile p){if(!NativeApp.CanLaunch(NativeApp.Running(store)))throw new Exception("Close ChatGPT and Codex sessions before this server check. Your account will not be switched.");
  using(var rpc=new NativeRpc(store,p)){var account=rpc.Call("account/read",new{refreshToken=false});var detail=FeatureJson.Obj(FeatureJson.Get(account,"account"));if(FeatureJson.Str(detail,"type")!="chatgpt")throw new Exception("This profile is not signed into a ChatGPT account.");var result=Cached(store,p);string email=FeatureJson.Str(detail,"email");if(email.Length==0)throw new Exception("The native service returned no account identity. Verify in ChatGPT.");result.Email=email;result.Plan=FeatureJson.Str(detail,"planType");
   var sessions=rpc.Call("accountSession/list",new{refreshWorkspaceMetadata=true});string active=FeatureJson.Str(sessions,"activeSessionId");var items=FeatureJson.Get(sessions,"sessions") as object[];bool workspaceConfirmed=false;
   if(items!=null)foreach(var item in items){var row=FeatureJson.Obj(item);if(FeatureJson.Str(row,"sessionId")==active){string workspace=FeatureJson.Str(row,"selectedWorkspaceAccountId");if(workspace.Length>0){result.Workspace=workspace;workspaceConfirmed=true;}}}
   result.Verified=workspaceConfirmed;result.State=workspaceConfirmed?"Account and workspace reported by native service":"Account reported by native service; workspace is cached";result.CredentialStamp=Stamp(store,p);ApplyExpected(result,p);return result;
  }
 }
}
public class HealthReport {public string Text;public bool Success;}
public static class HealthService {
 static HashSet<string> Listed(NativeRpc rpc,string method){var ids=new HashSet<string>();string cursor=null;var seen=new HashSet<string>();for(int page=0;page<100;page++){object parameters=method=="thread/list"?(object)new{limit=100,currentAccountOnly=false,useStateDbOnly=true,modelProviders=new string[0],cursor=cursor}:new{limit=100,cursor=cursor};var result=rpc.Call(method,parameters);var data=FeatureJson.Get(result,"data")as object[];if(data==null)throw new Exception("Native history listing returned an unexpected response.");foreach(var row in data){string id=FeatureJson.Str(FeatureJson.Obj(row),"id");if(id.Length>0)ids.Add(id);}string next=FeatureJson.Str(result,"nextCursor");if(next.Length==0)return ids;if(!seen.Add(next))throw new Exception("Native history pagination repeated a cursor.");cursor=next;}throw new Exception("History listing exceeded 10,000 items. Check was incomplete, not a pass.");}
 public static HealthReport History(ProfileStore store,Profile p){if(!NativeApp.CanLaunch(NativeApp.Running(store)))throw new Exception("Close ChatGPT and Codex sessions before checking history.");return HistoryCore(store,p);}
 static HealthReport HistoryCore(ProfileStore store,Profile p){SharedState.ValidateConfig(Path.Combine(store.Root,p.Folder,"codex","config.toml"));var expectedThreads=SharedState.SampleIds(store.State.SharedStatePath,"threads",20);var expectedProjects=SharedState.SampleIds(store.State.SharedStatePath,"projects",10000);long threads=SharedState.Count(store.State.SharedStatePath,"threads"),projects=SharedState.Count(store.State.SharedStatePath,"projects");
 using(var rpc=new NativeRpc(store,p)){var listedThreads=Listed(rpc,"thread/list");var listedProjects=Listed(rpc,"project/list");int missingThreads=expectedThreads.Count(x=>!listedThreads.Contains(x)),missingProjects=expectedProjects.Count(x=>!listedProjects.Contains(x));int readable=0;foreach(string id in expectedThreads.Where(listedThreads.Contains)){var read=rpc.Call("thread/read",new{threadId=id,includeTurns=false});if(FeatureJson.Str(FeatureJson.Obj(FeatureJson.Get(read,"thread")),"id")==id)readable++;}bool completeProjects=projects<=10000;bool success=missingThreads==0&&missingProjects==0&&readable==expectedThreads.Count&&completeProjects;return new HealthReport{Success=success,Text="Shared store: "+threads+" chats and "+projects+" projects.\nNative lists: "+listedThreads.Count+" chats and "+listedProjects.Count+" projects.\nSampled visible non-archived chat IDs: "+expectedThreads.Count+"; matched and readable: "+readable+".\nProject IDs checked: "+expectedProjects.Count+"; missing: "+missingProjects+".\n"+(success?"Sampled shared chat IDs and project IDs match.":"History visibility differs or the check is incomplete. Account/source filtering may apply.")+"\nEmpty stores have no content to verify. This does not confirm GUI visibility or permission to continue every chat."};}}
 public static bool WindowOpened(ProfileStore store,Profile p){var ids=new HashSet<int>(NativeApp.Running(store).Where(x=>x.ProfileId==p.Id).Select(x=>x.Id));foreach(var process in Process.GetProcessesByName("ChatGPT"))using(process)if(ids.Contains(process.Id)&&process.MainWindowHandle!=IntPtr.Zero)return true;return false;}
}

public class SnapshotEntry {public string Name;public string Sha256;}
public class SnapshotManifest {public int Version=2;public string CreatedUtc;public string SourcePath;public List<SnapshotEntry> Files=new List<SnapshotEntry>();}
public static class BackupService {
 public static string Hash(string file){using(var s=File.OpenRead(file))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
 static string Root(ProfileStore store){return Path.Combine(store.Root,"ManagedBackups");}
 public static string[] List(ProfileStore store){return Directory.Exists(Root(store))?Directory.GetDirectories(Root(store)).Where(x=>File.Exists(Path.Combine(x,"manifest.json"))).OrderByDescending(x=>x).ToArray():new string[0];}
 public static void Idle(ProfileStore store){if(!NativeApp.CanLaunch(NativeApp.Running(store)))throw new Exception("Close all ChatGPT and Codex sessions first. History was left unchanged.");}
 public static string Create(ProfileStore store){return Snapshot(store,()=>Idle(store));}
 static string CreateCore(ProfileStore store){return Snapshot(store,()=>{});}
 static string Snapshot(ProfileStore store,Action idle){idle();string destination=Path.Combine(Root(store),DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(destination);var manifest=new SnapshotManifest{CreatedUtc=DateTime.UtcNow.ToString("o"),SourcePath=Path.GetFullPath(store.State.SharedStatePath)};
  if(!Directory.Exists(store.State.SharedStatePath))throw new Exception("The shared history folder does not exist yet.");
  foreach(string file in Directory.GetFiles(store.State.SharedStatePath,"*.sqlite")){string name=Path.GetFileName(file);if(name.StartsWith("logs_"))continue;string output=Path.Combine(destination,name);idle();SharedState.BackupDatabase(file,output);SharedState.CheckIntegrity(output);idle();manifest.Files.Add(new SnapshotEntry{Name=name,Sha256=Hash(output)});}
  if(manifest.Files.Count==0)throw new Exception("There are no local history databases to back up yet.");idle();FeatureJson.Write(Path.Combine(destination,"manifest.json"),manifest);return destination;
 }
 public static SnapshotManifest Validate(ProfileStore store,string snapshot){string path=Path.GetFullPath(snapshot),root=Path.GetFullPath(Root(store))+Path.DirectorySeparatorChar;if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!List(store).Contains(path,StringComparer.OrdinalIgnoreCase))throw new Exception("Select a backup from this launcher's backup list.");
  var manifest=FeatureJson.Json.Deserialize<SnapshotManifest>(File.ReadAllText(Path.Combine(path,"manifest.json")));if(manifest==null||manifest.Version!=2||!String.Equals(manifest.SourcePath,Path.GetFullPath(store.State.SharedStatePath),StringComparison.OrdinalIgnoreCase)||manifest.Files==null||manifest.Files.Count==0)throw new Exception("Invalid backup manifest.");var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var entry in manifest.Files){if(entry==null||!System.Text.RegularExpressions.Regex.IsMatch(entry.Name??"","^[a-z][a-z0-9_]*\\.sqlite$")||entry.Name.StartsWith("logs_")||!names.Add(entry.Name))throw new Exception("Invalid backup file entry.");string file=Path.Combine(path,entry.Name);if(!File.Exists(file)||!String.Equals(Hash(file),entry.Sha256,StringComparison.OrdinalIgnoreCase))throw new Exception("Backup checksum mismatch. Restore was blocked.");SharedState.CheckIntegrity(file);}return manifest;
 }
 public static void Restore(ProfileStore store,string snapshot){RestoreFiles(store,snapshot,()=>Idle(store));}
 static Action<string> RestoreFault=null;
 static void RestoreFiles(ProfileStore store,string snapshot,Action idle){idle();var manifest=Validate(store,snapshot);var current=Directory.GetFiles(store.State.SharedStatePath,"*.sqlite").Select(Path.GetFileName).Where(x=>!x.StartsWith("logs_")).OrderBy(x=>x).ToArray();var saved=manifest.Files.Select(x=>x.Name).OrderBy(x=>x).ToArray();if(!current.SequenceEqual(saved))throw new Exception("This backup belongs to a different database layout. Restore was blocked.");
 string safety=Snapshot(store,idle);string stages=Path.Combine(safety,"restore-staging");Directory.CreateDirectory(stages);foreach(var entry in manifest.Files)File.Copy(Path.Combine(snapshot,entry.Name),Path.Combine(stages,entry.Name));
 var touched=new List<string>();try{foreach(var entry in manifest.Files){idle();string target=Path.Combine(store.State.SharedStatePath,entry.Name);touched.Add(entry.Name);foreach(string suffix in new[]{"-wal","-shm"})if(File.Exists(target+suffix))File.Move(target+suffix,Path.Combine(stages,entry.Name+suffix+".original"));if(RestoreFault!=null)RestoreFault(entry.Name);File.Replace(Path.Combine(stages,entry.Name),target,null);SharedState.CheckIntegrity(target);}NativeApp.Log("Managed history restore completed. Pre-restore backup retained.");}
 catch{bool recovered=true;foreach(string name in touched){try{string target=Path.Combine(store.State.SharedStatePath,name);foreach(string suffix in new[]{"-wal","-shm"})if(File.Exists(target+suffix))File.Move(target+suffix,Path.Combine(stages,name+suffix+".failed-"+Guid.NewGuid().ToString("N")));string stage=target+".rollback-"+Guid.NewGuid().ToString("N");File.Copy(Path.Combine(safety,name),stage);File.Replace(stage,target,null);SharedState.CheckIntegrity(target);}catch{recovered=false;}}
 throw new Exception(recovered?"Restore stopped. All touched databases, including the interrupted file, were recovered from the pre-restore snapshot.":"Restore stopped and automatic recovery is incomplete. Do not open native clients. The pre-restore snapshot is retained in ManagedBackups for recovery.");}
 }

}
public class CompatibilityResult {public bool Passed;public string Version;public string Message;}
public static class CompatibilityService {
 public static CompatibilityResult Check(){string exe=NativeApp.FindApp(),root=Path.GetDirectoryName(exe),asar=Path.Combine(root,"resources","app.asar"),cli=Path.Combine(root,"resources","codex.exe");string version=new DirectoryInfo(root).Parent.Name;bool marker=false;
  if(File.Exists(asar)){using(var stream=File.OpenRead(asar)){byte[] buffer=new byte[1024*1024];string tail="";int n;while((n=stream.Read(buffer,0,buffer.Length))>0){string text=tail+Encoding.ASCII.GetString(buffer,0,n);if(text.Contains("CODEX_ELECTRON_USER_DATA_PATH")){marker=true;break;}tail=text.Substring(Math.Max(0,text.Length-128));}}}
  return new CompatibilityResult{Passed=marker&&File.Exists(cli),Version=version,Message=marker&&File.Exists(cli)?"Installed app found. Separate desktop-profile startup marker and native backend are present. Real account sign-in still needs checking.":"The native app layout or profile startup option changed. Launching is blocked until compatibility is reviewed."};
 }
}
public static class DiagnosticService {
 public static void Export(ProfileStore store,string output){var data=new Dictionary<string,object>();data["launcherVersion"]=ReleaseService.Version;data["createdUtc"]=DateTime.UtcNow.ToString("o");data["osVersion"]=Environment.OSVersion.Version.ToString();data["profileCount"]=store.State.Profiles.Count(x=>!x.Removed);data["sharedStoreExists"]=Directory.Exists(store.State.SharedStatePath);data["managedBackupCount"]=BackupService.List(store).Length;
  try{data["blockingProcessCount"]=NativeApp.Running(store).Count;}catch{data["processInspection"]="failed";}
  try{var result=CompatibilityService.Check();data["nativeVersion"]=result.Version;data["compatibilityMarkersPresent"]=result.Passed;}catch{data["nativeDiscovery"]="failed";}
  try{data["localChatCount"]=SharedState.Count(store.State.SharedStatePath,"threads");data["localProjectCount"]=SharedState.Count(store.State.SharedStatePath,"projects");}catch{data["historyInspection"]="failed";}
  FeatureJson.Write(output,data);
 }
}
public class ReleaseManifest {public string Version;public string Url;public string Sha256;}
public static class ReleaseService {
 public const string Version="5.1.0";
 static void Https(string value){Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!="https"||!String.IsNullOrEmpty(uri.UserInfo))throw new Exception("Updates require an HTTPS release URL.");}
 static byte[] Fetch(string url,int limit){
  ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
  for(int redirects=0;redirects<=5;redirects++){
   Https(url);var request=(HttpWebRequest)WebRequest.Create(url);request.AllowAutoRedirect=false;request.Timeout=15000;request.ReadWriteTimeout=15000;
   using(var response=(HttpWebResponse)request.GetResponse()){
    int status=(int)response.StatusCode;if(status==301||status==302||status==303||status==307||status==308){string location=response.Headers["Location"];if(String.IsNullOrEmpty(location))throw new Exception("Invalid update redirect.");url=new Uri(new Uri(url),location).AbsoluteUri;continue;}
    if(response.ContentLength>limit)throw new Exception("Update response exceeded its size limit.");
    using(var input=response.GetResponseStream())using(var output=new MemoryStream()){byte[] buffer=new byte[16384];int count;while((count=input.Read(buffer,0,buffer.Length))>0){if(output.Length+count>limit)throw new Exception("Update response exceeded its size limit.");output.Write(buffer,0,count);}return output.ToArray();}
   }
  }
  throw new Exception("Too many update redirects.");
 }
 public static ReleaseManifest Check(ProfileStore store){if(String.IsNullOrWhiteSpace(store.State.UpdateManifestUrl)||String.IsNullOrWhiteSpace(store.State.PublisherThumbprint))throw new Exception("Updates are not configured. Set an HTTPS manifest URL and trusted publisher certificate thumbprint in Tools > Update settings. Until then, install releases manually.");Https(store.State.UpdateManifestUrl);ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
  string json=Encoding.UTF8.GetString(Fetch(store.State.UpdateManifestUrl,32768)).TrimStart('\uFEFF');var manifest=FeatureJson.Json.Deserialize<ReleaseManifest>(json);System.Version version;if(manifest==null||!System.Version.TryParse(manifest.Version,out version)||!System.Text.RegularExpressions.Regex.IsMatch(manifest.Sha256??"","^[a-fA-F0-9]{64}$"))throw new Exception("Invalid release manifest.");Https(manifest.Url);return manifest;
 }
 public static string Download(ProfileStore store,ReleaseManifest manifest){Https(manifest.Url);if(String.IsNullOrWhiteSpace(store.State.PublisherThumbprint))throw new Exception("No trusted publisher configured.");string root=Path.Combine(store.Root,"Updates");Directory.CreateDirectory(root);string target=Path.Combine(root,"release-"+Guid.NewGuid().ToString("N")+".exe");File.WriteAllBytes(target,Fetch(manifest.Url,50*1024*1024));
  if(!String.Equals(BackupService.Hash(target),manifest.Sha256,StringComparison.OrdinalIgnoreCase))throw new Exception("Release checksum did not match. The download will not be run.");string expected=store.State.PublisherThumbprint.Replace(" ","").ToUpperInvariant();if(!System.Text.RegularExpressions.Regex.IsMatch(expected,"^[A-F0-9]{40}$"))throw new Exception("Invalid trusted certificate thumbprint.");
  var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.Arguments="-NoProfile -NonInteractive -Command \"$s=Get-AuthenticodeSignature -LiteralPath $env:PROFILE_RELEASE_FILE; if($s.Status -eq 'Valid' -and $s.SignerCertificate.Thumbprint -eq $env:PROFILE_RELEASE_PUBLISHER){exit 0}else{exit 1}\"";start.EnvironmentVariables["PROFILE_RELEASE_FILE"]=target;start.EnvironmentVariables["PROFILE_RELEASE_PUBLISHER"]=expected;
  using(var p=Process.Start(start)){var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();if(!p.WaitForExit(20000)){p.Kill();throw new Exception("Publisher signature verification timed out. The download will not be run.");}stdout.GetAwaiter().GetResult();stderr.GetAwaiter().GetResult();if(p.ExitCode!=0)throw new Exception("Publisher signature verification failed. The download will not be run.");}return target;
 }
}
