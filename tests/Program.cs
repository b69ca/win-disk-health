using WinDiskHealth;
using System.Text;
using System.Text.Json;

int passed = 0;
void Check(string name, Action test) { test(); passed++; Console.WriteLine($"PASS {name}"); }
void Assert(bool b) { if (!b) throw new Exception("assertion failed"); }
void Reject(Action a) { try { a(); } catch (Exception e) when(e is JsonException or ArgumentException or IOException or DecoderFallbackException) { return; } throw new Exception("expected rejection"); }
var healthy = new Snapshot(App.ToolName, 1, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), "fixture", [new(1,"0",0,[2])], []);
Snapshot Disk(ushort? h, ushort[]? o, string? id="0") => healthy with { Disks = [new(1,id,h,o)] };
Check("healthy observation", () => Assert(App.Analyze(healthy).Status == "observed"));
Check("warning and unhealthy", () => { foreach(ushort h in new ushort[]{1,2}) Assert(App.Analyze(Disk(h,[2])).Status == "review"); });
Check("unknown health is incomplete", () => { foreach(ushort? h in new ushort?[]{null,5,99}) Assert(App.Analyze(Disk(h,[2])).Status == "partial"); });
Check("empty and missing operational evidence", () => { Assert(App.Analyze(Disk(0,[])).Status == "partial"); Assert(App.Analyze(Disk(0,null)).Status == "partial"); });
Check("unknown and vendor operational states", () => { foreach(ushort c in new ushort[]{0,1,99}) Assert(App.Analyze(Disk(0,[c])).Status == "partial"); });
Check("all attention operational states", () => { foreach(ushort c in new ushort[]{3,4,5,6,7,8,9,10,12,13,14,15,16,18,19,0xD004,0xD005,0xD006,0xD007,0xD008}) Assert(App.Analyze(Disk(0,[c])).Status == "review"); });
Check("housekeeping and completed observations", () => Assert(App.Analyze(Disk(0,[2,11,17])).Status == "observed"));
Check("mixed states preserve attention", () => Assert(App.Analyze(Disk(0,[2,5,5])).Findings.Count == 1));
Check("partial takes precedence but retains findings", () => { var r=App.Analyze(Disk(2,[0,6])); Assert(r.Status=="partial" && r.Findings.Count==2); });
Check("zero disks incomplete", () => Assert(App.Analyze(healthy with { Disks=[] }).Status=="partial"));
Check("collection issues incomplete", () => { foreach(var issue in new[]{"truncated","collector-unavailable"}) Assert(App.Analyze(healthy with { Issues=[issue] }).Status=="partial"); });
Check("missing identity incomplete", () => Assert(App.Analyze(Disk(0,[2],null)).Status=="partial"));
Check("identity whitelist", () => { foreach(var id in new[]{"","disk-name","12345678901","0\n","\u001b[0m"}) Reject(()=>App.Validate(Disk(0,[2],id))); });
Check("schema tool time and source validation", () => { Reject(()=>App.Validate(healthy with{SchemaVersion=2})); Reject(()=>App.Validate(healthy with{Tool="other"})); Reject(()=>App.Validate(healthy with{CapturedAt=default})); Reject(()=>App.Validate(healthy with{Source="other"})); });
Check("row and array bounds", () => { Reject(()=>App.Validate(healthy with {Disks=[new(2,"0",0,[2])]})); Reject(()=>App.Validate(healthy with{Disks=new Disk[257]})); Reject(()=>App.Validate(Disk(0,new ushort[33]))); });
Check("nulls and issue whitelist", () => { Reject(()=>App.Validate(healthy with{Disks=null!})); Reject(()=>App.Validate(healthy with{Issues=["details"]})); Reject(()=>App.Validate(healthy with{Issues=["truncated","truncated"]})); });
var serialized=JsonSerializer.Serialize(healthy,App.JsonOptions);
Check("snapshot round trip",()=>Assert(JsonSerializer.Serialize(App.Decode(serialized),App.JsonOptions)==serialized));
Check("report replay recomputes findings",()=> {var r=App.Analyze(healthy) with{Status="fake",Findings=[new(1,"fake","fake")]};Assert(App.Analyze(App.Decode(JsonSerializer.Serialize(r,App.JsonOptions))).Status=="observed");});
Check("missing and extra JSON members rejected",()=> {Reject(()=>App.Decode(serialized.Replace("\"schemaVersion\": 1,","")));Reject(()=>App.Decode(serialized.Replace("\"schemaVersion\": 1,","\"schemaVersion\": 1,\"serialNumber\":\"synthetic\",")));});
Check("duplicates and wrong types rejected",()=> {Reject(()=>App.Decode(serialized.Replace("\"schemaVersion\": 1,","\"schemaVersion\": 1,\"schemaVersion\": 1,")));Reject(()=>App.Decode(serialized.Replace("\"healthStatus\": 0","\"healthStatus\": \"0\"")));Reject(()=>App.Decode("null"));});
Check("strict file encoding and size",()=> {string p=Path.GetTempFileName();try { foreach(var enc in new Encoding[]{new UTF8Encoding(true,true),new UnicodeEncoding(false,true,true),new UnicodeEncoding(true,true,true)}) {File.WriteAllText(p,serialized,enc);Assert(App.Load(p).Tool==App.ToolName);}File.WriteAllBytes(p,[0xFF,0xFA]);Reject(()=>App.Load(p));File.WriteAllBytes(p,new byte[App.MaxBytes+1]);Reject(()=>App.Load(p));}finally{File.Delete(p);} });
Check("CLI options",()=> {Assert(App.Parse(["--json","--from","fixture.json"]).Json);Assert(App.Parse(["--help"]).Help);foreach(var a in new[]{new[]{"--json","--json"},new[]{"--help","--json"},new[]{"--from"},new[]{"--from","--json"},new[]{"--unknown"},new[]{"--from","a","--from","b"}})Reject(()=>App.Parse(a));});
Check("labels",()=> {Assert(App.HealthLabel(5)=="Unknown");Assert(App.OperationalLabel(0xD004)=="Failed Media");Assert(!App.KnownOperational(65535));});
Console.WriteLine($"{passed} behavior groups passed.");
