namespace WebRecomp.Web.Services.Learning;

// Voice changes presentation, never evidence, targets, or the meaning of a lesson.
public static class CoachVoice
{
    public static string Speak(CoachProfile coach, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var body = text.Replace("This matters because ", "Here's why: ")
            .Replace("This is why ", "That's why ");
        return coach.Code switch
        {
            "NINA" => "Focus on the next action. " + body.Replace("when convenient", "when your schedule allows") + " Make the useful step a habit.",
            "MARCUS" => "Let's connect the mechanism to the result. " + body + " Check the evidence, then apply it consistently.",
            "GRANT" => "Build strength you can keep using for years. " + body + " Steady work beats a quick fix.",
            "CASSIE" => "Let's make this support your next session. " + body + " Fuel the work and keep your performance moving.",
            "OWEN" => "Here's something to put into practice. " + body + " Pick one useful change and build from there.",
            "AISHA" => "You can build a stronger body one decision at a time. " + body + " Own the next step.",
            "LUCIA" => "All right, let's put this to work! " + body.Replace("does not", "doesn't").Replace("cannot", "can't") + " Keep it practical and keep showing up.",
            _ => "Let's build strength together. " + body + " Take the next manageable step; consistency adds up."
        };
    }
}
