using System.Net.Mail;
using System.Reflection;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

/// <summary>
/// Sends the new-client welcome message through the application's EXISTING
/// IEmailService transport. This deliberately does not create another Graph
/// permission path or another sender identity.
///
/// The adapter uses reflection so it can reuse the current IEmailService
/// request contract without duplicating MicrosoftGraphEmailService.
/// </summary>
public sealed class FirstSignInWelcomeEmailService(
    IEmailService emailService,
    ILogger<FirstSignInWelcomeEmailService> logger)
{
    public sealed record WelcomeContent(
        string Subject,
        string Body);    public static WelcomeContent BuildContent(
        string? displayName)
    {
        var firstLine =
            string.IsNullOrWhiteSpace(displayName)
                ? "Welcome to 12 Week Recomp."
                : $"Welcome to 12 Week Recomp, {displayName.Trim()}.";

        var lines =
            new[]
            {
                firstLine,
                "",
                "12 Week Recomp is a learning program, not just a tracking app.",
                "",
                "It is a 12-week learning process designed to help you understand how your own body responds to training, food, recovery, and consistency - and to build habits you can use for life.",
                "",
                "THE KEY: CONSISTENCY, NOT PERFECTION",
                "",
                "The key is consistency, not perfection. Be dedicated to following the program for as many of the 12 weeks as you can. One imperfect day will not ruin your progress. Repeated days of useful activity, honest logging, and attention to your results are what create the learning.",
                "",
                "THE MOST IMPORTANT PRINCIPLE: MOVE YOUR BODY",
                "",
                "Activity is the driver of the program.",
                "",
                "Resistance training is especially important because lifting weights gives your body the signal to build or preserve muscle, improve strength, and develop a firmer, more defined physique.",
                "",
                "That does not mean every day should be a hard lifting day. Walking, Zone 2 cardio, conditioning, mobility, sports, and recovery movement all count.",
                "",
                "Do something purposeful with your body every day.",
                "",
                "YOUR ACTIVITY HELPS DETERMINE YOUR FUEL",
                "",
                "The individualized engine powering 12WR looks at the activity you plan, the activity you actually complete, your body measurements, nutrition, recovery information, and the other data you provide.",
                "",
                "It then develops your daily calorie and macronutrient prescription.",
                "",
                "The harder the activity you have planned, the more fuel you may need.",
                "",
                "Hard resistance and conditioning work can require more carbohydrate to support performance and restore glycogen. Easier or recovery days may require less.",
                "",
                "Use the fuel the program gives you.",
                "",
                "More food on a demanding training day is not automatically working against fat loss. Proper fueling can help you train harder, recover better, preserve muscle, and continue progressing.",
                "",
                "CHOOSE YOUR DIRECTION",
                "",
                "During setup, choose the recomp approach that best matches what you want to emphasize: fat loss, balanced recomp, or muscle gain.",
                "",
                "A fat-loss-focused plan emphasizes reducing body fat while protecting muscle. A muscle-gain-focused plan emphasizes building muscle and strength while limiting unnecessary fat gain. A balanced approach gives similar priority to both.",
                "",
                "WE ARE NOT JUST TRACKING SCALE WEIGHT",
                "",
                "12 Week Recomp is trying to understand fat loss and muscle gain or preservation at the same time.",
                "",
                "A lower scale number is not automatically better if muscle is being lost. A flat or slightly higher body weight can still represent excellent recomp progress when body fat is falling while lean mass improves.",
                "",
                "If your scale only measures body weight, use it. Weight is still useful and 12WR will make the best prescription and analysis possible from the information available.",
                "",
                "But without body-fat and muscle or lean-mass readings, the engine has less evidence about the kind of change taking place. That means body-composition analysis and individualized adjustments are less precise.",
                "",
                "A body-composition/BIA scale that estimates body fat and muscle or lean mass can give 12WR much more useful trend information. These scales are relatively inexpensive and widely available online. If you do not have one now, consider picking up an inexpensive compatible body-composition scale at some point during the program.",
                "",
                "Consumer BIA readings are not perfect. Hydration, glycogen, sodium, food volume, and other factors can move them from day to day. 12WR uses repeated readings as trend evidence rather than treating one day's number as literal fat or muscle gain.",
                "",
                "WEEKLY PLAN",
                "",
                "Go to Weekly Plan and enter the activities you realistically expect to perform for the coming week. Use the guidance on the page to build a week you can actually follow.",
                "",
                "Your Weekly Plan gives the engine advance notice of the training demand coming up and helps it prepare your nutrition prescription.",
                "",
                "THE 12WR LEARNING LOOP",
                "",
                "Plan -> Do -> Log -> Measure -> Learn -> Adapt",
                "",
                "EVERY MORNING: MEASURE",
                "",
                "ENTER YOUR MORNING SCALE READINGS",
                "",
                "Weigh yourself under reasonably consistent morning conditions and enter your daily scale readings.",
                "",
                "At minimum, enter body weight. When available, also enter body fat, muscle or lean mass, body water, visceral-fat rating, or other measurements your scale provides.",
                "",
                "WATCH THE TREND",
                "",
                "One day is information. Repeated days create evidence. The trend is what helps you and your coaches understand what is really working.",
                "",
                "DURING THE DAY: COMPLETE YOUR ACTIVITY",
                "",
                "Do the workout or activity you planned. If life changes and you do something different, do what makes sense and tell the system what actually happened.",
                "",
                "AFTER THE WORKOUT: LOG IT",
                "",
                "Go to Daily Entry and enter the activity you actually completed, including duration and effort when available.",
                "",
                "THROUGHOUT THE DAY: TRACK YOUR FOOD",
                "",
                "LOG NUTRITION AS ACCURATELY AS PRACTICAL",
                "",
                "Use the nutrition app of your choice to track what you eat throughout the day. Many useful options are available for free.",
                "",
                "At the end of the day - or the following morning - enter your daily calories, protein, carbohydrate, and fat totals into 12WR.",
                "",
                "YOU DO NOT HAVE TO ENTER EVERYTHING",
                "",
                "Give the system the best information you reasonably can. More complete data gives the engine more evidence, but 12WR is designed to make the best prescription and analysis possible from the information that is available.",
                "",
                "Honest imperfect data is more useful than perfect-looking data that is not accurate.",
                "",
                "YOUR DAILY CYCLE",
                "",
                "Measure -> Move -> Log -> Fuel -> Learn -> Adapt",
                "",
                "THE 12-WEEK COMMITMENT",
                "",
                "The real value comes from staying with the process long enough to learn your own body.",
                "",
                "After you have diligently collected a meaningful portion of the 12 weeks, completed your activity, paid attention to your nutrition, read the coaching messages, and watched your results, you should have a much clearer understanding of what your body needs and which habits actually work for you.",
                "",
                "The goal is to help you build repeatable activity and eating habits that can move you toward the body you want while supporting long-term strength, health, and quality of life.",
                "",
                "When the 12 weeks are over, you can continue with 12 Week Recomp for another term and keep refining the process.",
                "",
                "Or you can let it go.",
                "",
                "If you leave with the knowledge, habits, and confidence to make good decisions without us, we will be thrilled. That means the program did one of the most important things it was built to do: teach you the information many people wish they had learned years ago about how training, food, recovery, and their own body actually work together.",
                "",
                "The principles behind 12 Week Recomp - progressive resistance training, adequate protein, activity-matched fueling, sensible energy control, recovery, and consistent measurement - are evidence-based and have a strong track record when applied consistently.",
                "",
                "Individual results vary.",
                "",
                "Individual results still vary even when the program is followed closely. Starting point, health status, training quality, nutrition, recovery, and consistency all affect the rate and magnitude of change.",
                "",
                "Consistency beats perfection. Keep showing up.",
                "",
                "Welcome to your 12 weeks.",
                "",
                "12 Week Recomp",
                "Built on evidence. Designed to teach you how your body works."
            };

        return new WelcomeContent(
            Subject: "Welcome to 12 Week Recomp - learn your body for the next 12 weeks",
            Body: string.Join(
                Environment.NewLine,
                lines));
    }

    public async Task<bool> TrySendAsync(
        UserAccount user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (ShouldSkip(user))
            return false;

        var address =
            FirstUsableEmail(
                user.ContactEmail,
                user.EmailNormalized);

        if (address is null)
        {
            logger.LogInformation(
                "12WR welcome email skipped for user {UserId}: no usable email address.",
                user.UserId);

            return false;
        }

        var content =
            BuildContent(
                user.PreferredDisplayName
                ?? user.DisplayName);

        if (!TryResolveContract(
                address,
                content,
                cancellationToken,
                out var method,
                out var args,
                out var reason))
        {
            logger.LogWarning(
                "12WR welcome email could not adapt IEmailService contract: {Reason}",
                reason);

            return false;
        }

        try
        {
            var raw =
                method!.Invoke(
                    emailService,
                    args);

            var accepted =
                await AwaitAndInterpretAsync(
                    raw);

            if (accepted)
            {
                logger.LogInformation(
                    "12WR first-sign-in welcome email accepted for user {UserId}.",
                    user.UserId);
            }
            else
            {
                logger.LogWarning(
                    "12WR first-sign-in welcome email transport returned a non-success result for user {UserId}.",
                    user.UserId);
            }

            return accepted;
        }
        catch (TargetInvocationException ex)
        {
            logger.LogWarning(
                ex.InnerException ?? ex,
                "12WR first-sign-in welcome email failed for user {UserId}. Sign-in remains successful.",
                user.UserId);

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "12WR first-sign-in welcome email failed for user {UserId}. Sign-in remains successful.",
                user.UserId);

            return false;
        }
    }

    public static bool CanAdaptCurrentContract(
        out string reason)
    {
        return TryResolveContract(
            "newclient@example.com",
            BuildContent("New Client"),
            CancellationToken.None,
            out _,
            out _,
            out reason);
    }

    private static bool ShouldSkip(
        UserAccount user)
    {
        var subject =
            user.ExternalSubject?.Trim()
            ?? "";

        if (subject.Equals(
                "development-user",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (subject.StartsWith(
                "demo:",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string? FirstUsableEmail(
        params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            try
            {
                var parsed =
                    new MailAddress(
                        candidate.Trim());

                return parsed.Address;
            }
            catch
            {
                // Try the next available user identity value.
            }
        }

        return null;
    }

    private static bool TryResolveContract(
        string destination,
        WelcomeContent content,
        CancellationToken cancellationToken,
        out MethodInfo? selectedMethod,
        out object?[]? selectedArguments,
        out string reason)
    {
        selectedMethod = null;
        selectedArguments = null;

        var candidates =
            typeof(IEmailService)
                .GetMethods()
                .Where(x =>
                    x.Name.Contains(
                        "Send",
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ScoreMethod)
                .ToArray();

        foreach (var method in candidates)
        {
            var roles =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            if (!TryBuildArguments(
                    method.GetParameters(),
                    destination,
                    content,
                    cancellationToken,
                    roles,
                    out var args))
            {
                continue;
            }

            if (!roles.Contains("recipient")
                || !roles.Contains("subject")
                || !roles.Contains("body"))
            {
                continue;
            }

            if (!IsAwaitableReturn(method.ReturnType))
                continue;

            selectedMethod = method;
            selectedArguments = args;
            reason =
                $"Using IEmailService.{method.Name}.";

            return true;
        }

        reason =
            "No Send method exposed by IEmailService could be mapped to recipient + subject + body using the current contract.";

        return false;
    }

    private static int ScoreMethod(
        MethodInfo method)
    {
        var score = 0;

        if (method.Name.Equals(
                "SendAsync",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        if (method.Name.Contains(
                "Email",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        if (IsAwaitableReturn(method.ReturnType))
            score += 5;

        return score;
    }

    private static bool TryBuildArguments(
        ParameterInfo[] parameters,
        string destination,
        WelcomeContent content,
        CancellationToken cancellationToken,
        HashSet<string> roles,
        out object?[] args)
    {
        args =
            new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            if (!TryMapValue(
                    parameters[i].ParameterType,
                    parameters[i].Name ?? "",
                    destination,
                    content,
                    cancellationToken,
                    roles,
                    depth: 0,
                    out var value))
            {
                if (parameters[i].HasDefaultValue)
                {
                    value =
                        parameters[i].DefaultValue;
                }
                else
                {
                    return false;
                }
            }

            args[i] = value;
        }

        return true;
    }

    private static bool TryMapValue(
        Type type,
        string name,
        string destination,
        WelcomeContent content,
        CancellationToken cancellationToken,
        HashSet<string> roles,
        int depth,
        out object? value)
    {
        value = null;

        if (type == typeof(CancellationToken))
        {
            value = cancellationToken;
            return true;
        }

        var nullableType =
            Nullable.GetUnderlyingType(type);

        if (nullableType is not null)
        {
            return TryMapValue(
                nullableType,
                name,
                destination,
                content,
                cancellationToken,
                roles,
                depth,
                out value);
        }

        var normalized =
            new string(
                name
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToLowerInvariant)
                    .ToArray());

        if (type == typeof(string))
        {
            if (normalized.Contains("replytoaddress")
                || normalized.Contains("replyaddress"))
            {
                value = null;
                return true;
            }

            if (normalized.Contains("replytoname"))
            {
                value = "12 Week Recomp";
                return true;
            }

            if (normalized.Contains("subject"))
            {
                roles.Add("subject");
                value = content.Subject;
                return true;
            }

            if (normalized.Contains("body")
                || normalized.Contains("message")
                || normalized.Contains("content"))
            {
                roles.Add("body");
                value = content.Body;
                return true;
            }

            var recipientName =
                normalized == "to"
                || normalized == "recipient"
                || normalized == "destination"
                || normalized.Contains("toaddress")
                || normalized.Contains("toemail")
                || normalized.Contains("recipientaddress")
                || normalized.Contains("recipientemail")
                || normalized.Contains("destinationaddress")
                || normalized.Contains("destinationemail");

            if (recipientName)
            {
                roles.Add("recipient");
                value = destination;
                return true;
            }

            if (normalized == "email"
                || normalized == "emailaddress")
            {
                roles.Add("recipient");
                value = destination;
                return true;
            }

            if (normalized.Contains("sendername")
                || normalized.Contains("fromname"))
            {
                value = "12 Week Recomp";
                return true;
            }

            return false;
        }

        if (type == typeof(bool))
        {
            if (normalized.Contains("html"))
            {
                // Existing contact transport is known to work with text.
                value = false;
                return true;
            }

            if (normalized.Contains("enabled"))
            {
                value = true;
                return true;
            }

            value = false;
            return true;
        }

        if (type.IsEnum)
        {
            var names =
                Enum.GetNames(type);

            var preferred =
                names.FirstOrDefault(x =>
                    x.Equals(
                        "Text",
                        StringComparison.OrdinalIgnoreCase)
                    || x.Equals(
                        "PlainText",
                        StringComparison.OrdinalIgnoreCase));

            value =
                Enum.Parse(
                    type,
                    preferred ?? names.First());

            return true;
        }

        if (type == typeof(int))
        {
            value = 0;
            return true;
        }

        if (depth >= 2
            || type.IsPrimitive
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(Guid))
        {
            return false;
        }

        return TryBuildComplexValue(
            type,
            destination,
            content,
            cancellationToken,
            roles,
            depth + 1,
            out value);
    }

    private static bool TryBuildComplexValue(
        Type type,
        string destination,
        WelcomeContent content,
        CancellationToken cancellationToken,
        HashSet<string> roles,
        int depth,
        out object? value)
    {
        value = null;

        var constructors =
            type
                .GetConstructors(
                    BindingFlags.Public
                    | BindingFlags.Instance)
                .OrderByDescending(x =>
                    x.GetParameters().Length)
                .ToArray();

        foreach (var constructor in constructors)
        {
            var localRoles =
                new HashSet<string>(
                    roles,
                    StringComparer.OrdinalIgnoreCase);

            var parameters =
                constructor.GetParameters();

            var values =
                new object?[parameters.Length];

            var ok = true;

            for (var i = 0; i < parameters.Length; i++)
            {
                if (!TryMapValue(
                        parameters[i].ParameterType,
                        parameters[i].Name ?? "",
                        destination,
                        content,
                        cancellationToken,
                        localRoles,
                        depth,
                        out var item))
                {
                    if (parameters[i].HasDefaultValue)
                    {
                        item =
                            parameters[i].DefaultValue;
                    }
                    else
                    {
                        ok = false;
                        break;
                    }
                }

                values[i] = item;
            }

            if (!ok)
                continue;

            try
            {
                var instance =
                    constructor.Invoke(values);

                foreach (var role in localRoles)
                    roles.Add(role);

                value = instance;
                return true;
            }
            catch
            {
                // Try another constructor.
            }
        }

        var empty =
            type.GetConstructor(
                Type.EmptyTypes);

        if (empty is null)
            return false;

        try
        {
            var instance =
                empty.Invoke(null);

            var localRoles =
                new HashSet<string>(
                    roles,
                    StringComparer.OrdinalIgnoreCase);

            var setAny = false;

            foreach (var property in type.GetProperties(
                         BindingFlags.Public
                         | BindingFlags.Instance))
            {
                if (!property.CanWrite)
                    continue;

                if (!TryMapValue(
                        property.PropertyType,
                        property.Name,
                        destination,
                        content,
                        cancellationToken,
                        localRoles,
                        depth,
                        out var mapped))
                {
                    continue;
                }

                property.SetValue(
                    instance,
                    mapped);

                setAny = true;
            }

            if (!setAny)
                return false;

            foreach (var role in localRoles)
                roles.Add(role);

            value = instance;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAwaitableReturn(
        Type type)
    {
        if (typeof(Task).IsAssignableFrom(type))
            return true;

        if (type == typeof(ValueTask))
            return true;

        if (type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            return true;
        }

        return false;
    }

    private static async Task<bool> AwaitAndInterpretAsync(
        object? raw)
    {
        if (raw is null)
            return true;

        if (raw is Task task)
        {
            await task.ConfigureAwait(false);

            object? result = null;

            var resultProperty =
                task.GetType().GetProperty(
                    "Result",
                    BindingFlags.Public
                    | BindingFlags.Instance);

            if (resultProperty is not null)
            {
                result =
                    resultProperty.GetValue(task);
            }

            return InterpretResult(result);
        }

        if (raw is ValueTask valueTask)
        {
            await valueTask.ConfigureAwait(false);
            return true;
        }

        var type =
            raw.GetType();

        if (type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var asTask =
                type.GetMethod(
                    "AsTask",
                    BindingFlags.Public
                    | BindingFlags.Instance);

            if (asTask?.Invoke(raw, null) is Task genericTask)
            {
                await genericTask.ConfigureAwait(false);

                var resultProperty =
                    genericTask.GetType().GetProperty(
                        "Result",
                        BindingFlags.Public
                        | BindingFlags.Instance);

                var result =
                    resultProperty?.GetValue(genericTask);

                return InterpretResult(result);
            }
        }

        return false;
    }

    private static bool InterpretResult(
        object? result)
    {
        if (result is null)
            return true;

        if (result is bool boolean)
            return boolean;

        var type =
            result.GetType();

        foreach (var propertyName in new[]
                 {
                     "Success",
                     "Succeeded",
                     "IsSuccess",
                     "Accepted",
                     "Sent"
                 })
        {
            var property =
                type.GetProperty(
                    propertyName,
                    BindingFlags.Public
                    | BindingFlags.Instance
                    | BindingFlags.IgnoreCase);

            if (property?.PropertyType == typeof(bool)
                && property.GetValue(result) is bool value)
            {
                return value;
            }
        }

        // If the transport completed without throwing and does not expose an
        // explicit success flag, treat completion as acceptance.
        return true;
    }
}