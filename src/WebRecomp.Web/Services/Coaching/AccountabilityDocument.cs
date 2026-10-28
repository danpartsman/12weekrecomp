using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace WebRecomp.Web.Services.Coaching;

public static class AccountabilityDocument
{
    public const string Policy = "PER_PERIOD_ALL_CHEAT_OCCURRENCES_V3";
    public const string ExcusedPolicy = "Coach-approved dated excuses waive adherence assessments and incentives, but not contract payments. Missing evidence remains pending. No extra reward for under-eating. Explicit Snap completion and extra-rep rewards require assigned weight and on-time completion.";
    public static readonly JsonSerializerOptions Json = CreateJson();
    private static JsonSerializerOptions CreateJson(){var options=new JsonSerializerOptions(JsonSerializerDefaults.Web){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,RespectNullableAnnotations=true};options.Converters.Add(new UtcOffsetConverter());return options;}
    private sealed class UtcOffsetConverter:JsonConverter<DateTimeOffset>{public override DateTimeOffset Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>reader.GetDateTimeOffset();public override void Write(Utf8JsonWriter writer,DateTimeOffset value,JsonSerializerOptions options)=>writer.WriteStringValue(value.UtcDateTime.ToString("O"));}
    public static readonly string[] RuleIds = ["calories","protein","carbs","fat","training","recovery","logging","checkin","cheat-foods","missed-review"];
    public static readonly string[] Frequencies = ["daily","weekly","monthly"];
    public static readonly decimal[] Amounts = [0,1,2,5,10,15,20,25,30,50,75,100];
    public static AccountabilityState Parse(string json)
    {
        if (json.Length > 1048576) throw new ArgumentException("The coaching document is too large.");
        try { var state=JsonSerializer.Deserialize<AccountabilityState>(json,Json) ?? throw new JsonException(); Validate(state); return state; }
        catch(Exception e) when(e is JsonException or NullReferenceException) { throw new ArgumentException("The coaching document has invalid fields."); }
    }
    public static string Serialize(AccountabilityState state) => JsonSerializer.Serialize(state,Json);
    public static bool Money(decimal n, decimal max=10000) => n >= 0 && n <= max && decimal.Round(n,2)==n;
    public static bool Id(string? id) => id is not null && Regex.IsMatch(id,"^[a-zA-Z0-9-]{1,64}$",RegexOptions.CultureInvariant);
    public static DateOnly Date(string value) => DateOnly.TryParseExact(value,"yyyy-MM-dd",out var d) ? d : throw new ArgumentException("Use a valid date.");
    public static void Validate(AccountabilityState s)
    {
        if(s.Version!=2 || s.Rules.Count!=10 || !s.Rules.Select(x=>x.Id).SequenceEqual(RuleIds)) throw new ArgumentException("Choose the ten supported accountability rules.");
        for(var i=0;i<s.Rules.Count;i++)
        {
            var r=s.Rules[i];
            if(!new[]{"fine","incentive"}.Contains(r.Type)||!Money(r.Amount)||!Frequencies.Contains(r.Frequency)||!Money(r.Min)||!Money(r.Max)||r.Min>r.Max||r.Lower>r.Upper) throw new ArgumentException("Check rule amounts and assessment periods.");
            if(i<4 ? r.Lower < -100 || r.Lower>0 || r.Upper<0 || r.Upper>500 : r.Lower<0 || r.Upper>1000 || decimal.Truncate(r.Lower)!=r.Lower || decimal.Truncate(r.Upper)!=r.Upper) throw new ArgumentException("Check rule criteria.");
            if(r.Id=="cheat-foods" && (r.Type!="fine"||r.Lower!=0||r.Upper!=0||(r.Assigned&&r.Amount<=0))) throw new ArgumentException("Every cheat-food occurrence must use a positive paid rate without an allowance.");
        }
        var a=s.Agreement;
        if(a.Mode is not ("open" or "weeks")||a.Weeks is <1 or >52) throw new ArgumentException("Choose an open-ended agreement or 1–52 weeks.");
        if(a.Start!="") {var start=Date(a.Start);if(start.Year is <1900 or >2100 ||(a.EndedOn is not null&&Date(a.EndedOn)<start))throw new ArgumentException("Check agreement dates.");}
        else if(s.Enabled||s.SnapWorkouts.Count>0||a.EndedOn is not null)throw new ArgumentException("Save an agreement start date first.");
        var p=s.Payment;
        if(!new[]{"bank-transfer","card-external","cash","check","other"}.Contains(p.Method)||!Frequencies.Contains(p.Frequency)||!Enum.TryParse<DayOfWeek>(p.Day,out _)||!Money(p.Amount)||p.GraceDays is <0 or >30||p.LateBasis is not ("amount" or "percent")||!Money(p.LateValue,p.LateBasis=="percent"?100:10000)||!Frequencies.Contains(p.LateFrequency)||!Money(p.LateCap)||p.Instructions.Length>300)throw new ArgumentException("Check payment and late-fee terms.");
        if(s.Excused.Count>200||s.Observations.Count>200)throw new ArgumentException("Too many reviewed dates.");
        foreach(var date in s.Excused.Keys)Date(date);
        foreach(var day in s.Observations){Date(day.Key);foreach(var pair in day.Value)if(!RuleIds.Skip(4).Contains(pair.Key)||pair.Value is <0 or >1000)throw new ArgumentException("Check reviewed counts.");}
        if(s.SnapWorkouts.Count>50||s.SnapResults.Count>50||s.SnapWorkouts.Select(x=>x.Id).Distinct().Count()!=s.SnapWorkouts.Count)throw new ArgumentException("Check Snap Workout assignments.");
        foreach(var w in s.SnapWorkouts)
        {
            Date(w.Date);
            if(!Id(w.Id)||string.IsNullOrWhiteSpace(w.Name)||w.Name.Length>100||!Money(w.Cap)||w.Exercises.Count is <1 or >15||w.Exercises.Select(x=>x.Id).Distinct().Count()!=w.Exercises.Count)throw new ArgumentException("Check Snap Workout targets.");
            if(w.DeadlineUtc.HasValue&&(!w.CreatedAtUtc.HasValue||w.DeadlineUtc<=w.CreatedAtUtc||w.DeadlineUtc.Value.Offset!=TimeSpan.Zero))throw new ArgumentException("Check the workout deadline.");
            foreach(var e in w.Exercises) if(!Id(e.Id)||!(Guid.TryParse(e.CatalogId,out _)||(e.CatalogId==""&&e.MaxWeight.HasValue&&e.Weight==0))||string.IsNullOrWhiteSpace(e.Name)||e.Name.Length>250||e.Sets is <1 or >10||e.Reps is <1 or >100||!Money(e.Weight,2000)||(e.MaxWeight.HasValue&&(!Money(e.MaxWeight.Value,2000)||e.MaxWeight<e.Weight))||e.Type is not ("fine" or "incentive")||e.Basis is not ("completed-reps" or "completed-sets" or "incomplete-reps" or "incomplete-sets")||!Money(e.Rate,100)||!Money(e.CompletionReward??0)||!Money(e.ExtraRepRate??0,100))throw new ArgumentException("Check exercise targets and rates.");
        }
        foreach(var pair in s.SnapResults)ValidateResult(s,pair.Key,pair.Value);
    }
    public static void ValidateResult(AccountabilityState state,string id,AccountabilityResult result)
    {
        var w=state.SnapWorkouts.SingleOrDefault(x=>x.Id==id)??throw new ArgumentException("This workout is not assigned.");
        if(result.Sets is null || result.Sets.Count!=w.Exercises.Count || (result.Finalized&&w.DeadlineUtc.HasValue&&!result.CompletedAtUtc.HasValue))throw new ArgumentException("Enter final completion time and all sets.");
        foreach(var e in w.Exercises)
        {
            if(!result.Sets.TryGetValue(e.Id,out var sets)||sets is null||sets.Count<e.Sets||sets.Count>20)throw new ArgumentException("Check completed sets.");
            foreach(var set in sets)if(set is null||(set.Reps is null&&result.Finalized)||(set.Weight is null&&result.Finalized)||(set.Reps is not null&&(set.Reps<0||set.Reps>1000))||(set.Weight is not null&&!Money(set.Weight.Value,2000)))throw new ArgumentException("Enter valid completed reps and weight.");
        }
    }
    public static string ExpectedTerms(AccountabilityState s)
    {
        var root=new JsonObject { ["version"]=2,["assessmentPolicy"]=Policy,["agreement"]=JsonSerializer.SerializeToNode(s.Agreement,Json),["enabled"]=s.Enabled,["rules"]=JsonSerializer.SerializeToNode(s.Rules,Json),["payment"]=JsonSerializer.SerializeToNode(s.Payment,Json) };
        if(s.SnapWorkouts.Count>0)root["snapWorkouts"]=JsonSerializer.SerializeToNode(s.SnapWorkouts,Json);
        root["excusedPolicy"]=ExcusedPolicy;return root.ToJsonString();
    }
    public static string Canonical(string json)
    {
        using var doc=JsonDocument.Parse(json); using var stream=new MemoryStream();
        using(var writer=new Utf8JsonWriter(stream)) Write(doc.RootElement,writer,"");
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        static void Write(JsonElement value,Utf8JsonWriter writer,string key)
        {
            switch(value.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();foreach(var item in value.EnumerateObject().Where(x=>x.Value.ValueKind!=JsonValueKind.Null).OrderBy(x=>x.Name,StringComparer.Ordinal)){writer.WritePropertyName(item.Name);Write(item.Value,writer,item.Name);}writer.WriteEndObject();break;
                case JsonValueKind.Array: writer.WriteStartArray();foreach(var item in value.EnumerateArray())Write(item,writer,"");writer.WriteEndArray();break;
                case JsonValueKind.Number: writer.WriteRawValue(value.GetDecimal().ToString("G29",System.Globalization.CultureInfo.InvariantCulture));break;
                case JsonValueKind.String:
                    var text=value.GetString();
                    if(key is "deadlineUtc" or "createdAtUtc" or "completedAtUtc" && DateTimeOffset.TryParse(text,out var stamp))text=stamp.ToUniversalTime().ToString("O");
                    writer.WriteStringValue(text);break;
                default:value.WriteTo(writer);break;
            }
        }
    }
    public static string HashTerms(string json)=>AccountabilityApprovalReceipt.Hash(Canonical(json));
    public static void ValidateTerms(AccountabilityState s,string terms)
    {
        if(terms.Length>32768)throw new ArgumentException("The agreement has too many terms.");
        try {if(Canonical(ExpectedTerms(s)) != Canonical(terms))throw new ArgumentException("Displayed terms do not match the saved configuration.");}
        catch(JsonException){throw new ArgumentException("Use valid agreement terms.");}
    }
}
public sealed class AccountabilityState {public int Version{get;set;}=2;public bool Enabled{get;set;}public List<AccountabilityRule> Rules{get;set;}=[];public AccountabilityPayment Payment{get;set;}=new();public AccountabilityDates Agreement{get;set;}=new();public Dictionary<string,bool> Excused{get;set;}=[];public Dictionary<string,Dictionary<string,int>> Observations{get;set;}=[];public List<AccountabilityWorkout> SnapWorkouts{get;set;}=[];public Dictionary<string,AccountabilityResult> SnapResults{get;set;}=[];}
public sealed class AccountabilityRule {public string Id{get;set;}="";public bool Assigned{get;set;}public string Type{get;set;}="fine";public decimal Amount{get;set;}public string Frequency{get;set;}="daily";public decimal Lower{get;set;}public decimal Upper{get;set;}public decimal Min{get;set;}public decimal Max{get;set;}=100;}
public sealed class AccountabilityDates {public string Start{get;set;}="";public string Mode{get;set;}="open";public int Weeks{get;set;}=12;public string? EndedOn{get;set;}}
public sealed class AccountabilityPayment {public string Method{get;set;}="bank-transfer";public string Frequency{get;set;}="weekly";public string Day{get;set;}="Monday";public decimal Amount{get;set;}public int GraceDays{get;set;}=3;public string LateBasis{get;set;}="amount";public decimal LateValue{get;set;}public string LateFrequency{get;set;}="weekly";public decimal LateCap{get;set;}=100;public string Instructions{get;set;}="";}
public sealed class AccountabilityWorkout {public string Id{get;set;}="";public string Date{get;set;}="";public string Name{get;set;}="";public decimal Cap{get;set;}=100;public DateTimeOffset? DeadlineUtc{get;set;}public DateTimeOffset? CreatedAtUtc{get;set;}public List<AccountabilityExercise> Exercises{get;set;}=[];}
public sealed class AccountabilityExercise {public string Id{get;set;}="";public string CatalogId{get;set;}="";public string Name{get;set;}="";public int Sets{get;set;}public int Reps{get;set;}public decimal Weight{get;set;}[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public decimal? MaxWeight{get;set;}public string Type{get;set;}="fine";public string Basis{get;set;}="incomplete-reps";public decimal Rate{get;set;}public decimal? CompletionReward{get;set;}public decimal? ExtraRepRate{get;set;}}
public sealed class AccountabilityResult {public bool Finalized{get;set;}public DateTimeOffset? CompletedAtUtc{get;set;}public Dictionary<string,List<AccountabilitySet>> Sets{get;set;}=[];}
public sealed class AccountabilitySet {public int? Reps{get;set;}public decimal? Weight{get;set;}}
