using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.ViewModels;
namespace WebRecomp.Web.Services.Coaching;

public sealed class CoachCenterService(RecompDbContext db,IConfiguration? configuration=null)
{
    private static readonly string[] CodeWords = "ABLE ACID ACME ACRE AGED ALSO AMEN ARCH AREA ARID ARMY ATOM AUNT AUTO AWAY AXIS BABY BACK BAKE BALD BALL BAND BANK BARN BASE BATH BEAM BEAN BEAR BEAT BELL BELT BEND BENT BEST BETA BIKE BIND BIRD BITE BLUE BOAT BOLD BOLT BOND BONE BOOK BOOM BOOT BOWL BRIM BUCK BULB BUMP BURN BUSH BUSY CAFE CAGE CAKE CALM CAMP CANE CAPE CARD CARE CART CASE CASH CAST CAVE CELL CHEF CHIP CITY CLAY CLIP CLUB COAL COAT CODE COIL COLD COLT COME COOK COOL CORE CORN COST COZY CREW CROP CROW CUBE CURE CURL CUTE DARE DARK DART DASH DATA DATE DAWN DEAL DEAR DECK DEEP DEER DELI DENT DESK DICE DIME DINE DIRT DIVE DOCK DOME DONE DOOR DOVE DOWN DRAW DROP DRUM DUCK DUNE DUSK DUST DUTY EACH EARN EAST EASY ECHO EDGE EDIT ELSE EPIC EVEN EVER EXAM FACE FACT FAIR FALL FARM FAST FATE FEED FEEL FERN FILE FILL FILM FIND FINE FIRE FIRM FISH FIVE FLAG FLAT FLEX FLIP FLOW FOAM FOLD FOLK FOOD FOOT FORD FORM FORT FOUR FREE FROG FUEL FULL FUND FUSE GAIN GAME GATE GEAR GIFT GIRL GIVE GLAD GLOW GOAL GOAT GOLD GOLF GOOD GRAB GRAY GRID GRIN GRIP GROW GULF GUST HALF HALL HALO HAND HARD HARE HARP HAWK HAZE HEAD HEAL HEAP HEAT HELP HERB HERO HIGH HIKE HILL HINT HIVE HOLD HOME HOOD HOOK HOPE HORN HOST HOUR HUNT HUSH IDEA INCH IRIS IRON ISLE ITEM JADE JAZZ JEEP JOIN JOKE JOLT JUMP JUNE JURY JUST KEEN KEEP KELP KEPT KICK KIND KING KITE KIWI KNEE KNIT KNOW LACE LAKE LAMB LAMP LAND LANE LAST LATE LAWN LEAD LEAF LEAN LENS LIFT LIKE LILY LIME LINE LINK LION LIST LIVE LOAD LOAF LOAN LOCK LOFT LOGO LONG LOOK LOOP LOVE LUCK LUSH LYNX MADE MAIL MAIN MAKE MALT MANY MAPS MARK MASK MAST MATE MAZE MEAL MEAN MEET MELT MEMO MESH MILD MILE MILK MINT MIST MODE MOON MORE MOST MOVE MUCH MULE MUSE NAME NAVY NEAR NEAT NEST NEWS NEXT NICE NINE NODE NOON NORM NOTE NOVA OATH OATS ONCE ONLY OPEN ORAL OVAL OVEN OVER PACE PACK PAGE PAID PAIR PALM PARK PART PAST PATH PEAK PEAR PEEL PIER PINE PINK PIPE PLAN PLAY PLOT PLUM PLUS POEM POLE POND POOL PORT POSE POST POUR PULL PUMA PURE PUSH QUAD QUIZ RACE RACK RAIL RAIN RAMP RANK RARE RATE READ REAL REED REEF RELY RENT REST RICE RICH RIDE RIFT RING RISE RISK ROAD ROAM ROCK ROLE ROLL ROOF ROOM ROOT ROPE ROSE ROWS RUBY RULE RUSH RUST SAFE SAGE SAIL SALT SAME SAND SAVE SEAL SEAM SEAT SEED SEEK SEEN SELF SEND SHED SHIP SHOE SHOP SHOW SIDE SIGN SILK SING SITE SIZE SKIN SKIP SLIM SLIP SLOT SLOW SNAP SNOW SOAR SOAP SOFA SOFT SOIL SOLE SOME SONG SOON SOUP SPAN SPIN SPOT STAR STAY STEM STEP STIR STOP SUCH SUIT SURE SURF SWAN SWIM TACK TAIL TAKE TALE TALK TALL TAME TANK TAPE TASK TEAM TEAR TELL TENT TERM TEST TEXT THAN THAT THEM THEN THEY THIN THIS TIDE TIDY TILE TIME TINY TIRE TOAD TONE TOOL TOPS TORN TOUR TOWN TRAY TREE TREK TRIM TRIO TRUE TUBE TUNA TUNE TURN TWIN TYPE UNIT UPON USED USER VAST VEIL VENT VERY VIEW VINE VOTE WADE WAGE WAIT WAKE WALK WALL WARM WASH WAVE WAYS WEAR WEEK WELL WEST WHAT WHEN WIDE WILD WILL WIND WINE WING WIRE WISE WISH WOLF WOOD WOOL WORD WORK WRAP YARD YEAR YOGA YOUR ZERO ZEST ZINC ZONE ZOOM".Split(' ');
    public static string CreateShortCode() => CodeWords[RandomNumberGenerator.GetInt32(CodeWords.Length)] + RandomNumberGenerator.GetInt32(100).ToString("D2");
    public static string HashCode(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant())));
    public Task<bool> CanAccessAsync(Guid coachUserId, Guid clientId, CancellationToken ct = default) =>
        db.CoachClientRelationships.AnyAsync(x => x.CoachUserId == coachUserId && x.ClientId == clientId
            && x.StatusCode == "ACTIVE" && x.RevokedAtUtc == null, ct);

    public async Task<Dictionary<Guid,CoachSnapStatus>> SnapStatusesAsync(Guid coach,CancellationToken ct)
    {
        var ids=from link in db.CoachClientRelationships join client in db.Clients on link.ClientId equals client.ClientId
                where link.CoachUserId==coach&&link.StatusCode=="ACTIVE"&&link.RevokedAtUtc==null&&client.ClientStatusCode=="ACTIVE" select client.ClientId;
        var rows=await db.CoachAccountabilityAgreements.AsNoTracking().Where(x=>ids.Contains(x.ClientId)).Select(x=>new{x.ClientId,x.StateJson}).ToListAsync(ct);
        return rows.ToDictionary(x=>x.ClientId,x=>SnapStatus(VisibleState(x.StateJson)));
    }
    private AccountabilityState VisibleState(string json)
    {
        var state=AccountabilityDocument.Parse(json);
        if(SimpleCoaching.Enabled(configuration))state.SnapWorkouts=state.SnapWorkouts.Where(w=>w.Simple).ToList();
        return state;
    }
    public static CoachSnapStatus SnapStatus(AccountabilityState state)
    {
        var ordered=state.SnapWorkouts.OrderByDescending(w=>state.SnapResults.GetValueOrDefault(w.Id)?.Finalized==true)
            .ThenByDescending(w=>state.SnapResults.GetValueOrDefault(w.Id)?.CompletedAtUtc).ThenByDescending(w=>w.Date).ThenByDescending(w=>w.CreatedAtUtc).ToList();
        var latest=ordered.FirstOrDefault();var result=latest is null?null:state.SnapResults.GetValueOrDefault(latest.Id);
        return new(state.SnapWorkouts.Count,state.SnapWorkouts.Count(w=>state.SnapResults.GetValueOrDefault(w.Id)?.Finalized==true),
            state.SnapWorkouts.Count(w=>SnapWorkoutLibrary.Completed(w,state.SnapResults.GetValueOrDefault(w.Id))),latest?.Id,latest?.Name,
            latest is null?null:SnapWorkoutLibrary.Completed(latest,result)?"Completed":result?.Finalized==true?"Submitted · targets incomplete":result is not null?"Progress saved":"Assigned");
    }
    public async Task<CoachCenterViewModel> BuildAsync(UserAccount user, CancellationToken ct)
    {
        var profile = await db.CoachProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.UserId, ct);
        var ids = db.CoachClientRelationships.Where(x => x.CoachUserId == user.UserId && x.StatusCode == "ACTIVE" && x.RevokedAtUtc == null).Select(x => x.ClientId);
        var clients = await db.Clients.AsNoTracking().Where(x => ids.Contains(x.ClientId) && x.ClientStatusCode == "ACTIVE").OrderBy(x => x.ClientName).ToListAsync(ct);
        var snaps=await SnapStatusesAsync(user.UserId,ct);
        var roster = new List<CoachRosterClient>();
        foreach (var client in clients)
        {
            var goal = await db.Programs.Where(x => x.ClientId == client.ClientId && x.ProgramStatusCode == "ACTIVE").OrderByDescending(x => x.StartDate).Select(x => x.GoalCode).FirstOrDefaultAsync(ct);
            var last = await db.WorkoutSessions.Where(x => x.ClientId == client.ClientId && x.CompletedFlag).OrderByDescending(x => x.WorkoutDate).Select(x => (DateOnly?)x.WorkoutDate).FirstOrDefaultAsync(ct);
            roster.Add(new(client.ClientId, client.ClientName, goal, last){Snap=snaps.GetValueOrDefault(client.ClientId)??new()});
        }
        return new(new() { DisplayName = profile?.DisplayName ?? user.PreferredDisplayName ?? user.DisplayName ?? "Coach", Bio = profile?.Bio, Specialties = profile?.Specialties }, roster,
            await db.CoachClientRelationships.CountAsync(x => x.CoachUserId == user.UserId && x.StatusCode == "INVITED" && x.InviteExpiresAtUtc > DateTime.UtcNow, ct));
    }

    public async Task<string> InviteAsync(Guid coachUserId, CancellationToken ct)
    {
        // Reserve codes under the same retry-safe transaction as expiration cleanup.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
        db.ChangeTracker.Clear();
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var expired = await db.CoachClientRelationships.Where(x => x.StatusCode == "INVITED" && x.InviteExpiresAtUtc <= DateTime.UtcNow).ToListAsync(ct);
        foreach (var row in expired) { row.StatusCode = "REVOKED"; row.RevokedAtUtc = DateTime.UtcNow; row.InviteTokenHash = null; }
        await db.SaveChangesAsync(ct);
        string code;
        do { code = CreateShortCode(); }
        while (await db.CoachClientRelationships.AnyAsync(x => x.InviteTokenHash == HashCode(code), ct));
        db.CoachClientRelationships.Add(new() { CoachClientRelationshipId = Guid.NewGuid(), CoachUserId = coachUserId,
            InviteTokenHash = HashCode(code), CreatedAtUtc = DateTime.UtcNow, InviteExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return code;
        });
    }

    public async Task<bool> AcceptAsync(Guid clientId, string code, CancellationToken ct)
    {
        var hash = HashCode(code);
        var strategy = db.Database.CreateExecutionStrategy();
        Guid? acceptedInviteId = null;
        return await strategy.ExecuteAsync(async () =>
        {
        db.ChangeTracker.Clear();
        if (acceptedInviteId.HasValue && await db.CoachClientRelationships.AnyAsync(x => x.CoachClientRelationshipId == acceptedInviteId && x.ClientId == clientId && x.StatusCode == "ACTIVE", ct)) return true;
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        var invite = await db.CoachClientRelationships.SingleOrDefaultAsync(x => x.InviteTokenHash == hash && x.StatusCode == "INVITED" && x.InviteExpiresAtUtc > DateTime.UtcNow, ct);
        if (invite is null) return false;
        if (await CanAccessAsync(invite.CoachUserId, clientId, ct)) return false;
        acceptedInviteId = invite.CoachClientRelationshipId;
        invite.ClientId = clientId;
        invite.StatusCode = "ACTIVE";
        invite.AcceptedAtUtc = DateTime.UtcNow;
        invite.InviteTokenHash = null; // A consumed code can never connect a second client.
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return true;
        });
    }
}
