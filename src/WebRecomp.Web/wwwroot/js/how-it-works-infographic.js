(() => {
    "use strict";

    const hero = document.querySelector(".hiw-hero h1");
    if (hero) {
        hero.innerHTML = 'LEARN IT.<br><span class="green">LIVE IT.</span>';
    }

    const heroSub = document.querySelector(".hiw-hero-sub");
    if (heroSub) {
        heroSub.innerHTML =
            'Give <strong>The 12 Week Recomp System</strong> your starting profile and the way you prefer to move each week. Then practice three simple actions every day.';
    }

    const setupSpan = document.querySelector(".hiw-setup span");
    if (setupSpan) {
        setupSpan.textContent =
            "Tell The 12 Week Recomp System where you are and how you like to train. You can change it later. The system handles the calculations from there.";
    }

    const labels = Array.from(document.querySelectorAll(".hiw-section-label"));
    const daily = labels.find(section =>
        section.querySelector("small")?.textContent.trim() === "YOUR DAILY LOOP"
    );

    if (daily) {
        const heading = daily.querySelector("h2");
        const copy = daily.querySelector("p");

        if (heading) {
            heading.textContent = "Practice Doing 3 Things Every Day.";
        }

        if (copy) {
            copy.textContent =
                "Then you will have replaced society’s unhealthy habits with real living habits for life!";
        }
    }

    const shell = document.querySelector(".hiw-shell");
    if (shell) {
        const walker = document.createTreeWalker(shell, NodeFilter.SHOW_TEXT);
        let node;

        while ((node = walker.nextNode())) {
            if (node.nodeValue && node.nodeValue.includes("12WR")) {
                node.nodeValue = node.nodeValue.replaceAll(
                    "12WR",
                    "The 12 Week Recomp System"
                );
            }
        }
    }

    const img = document.getElementById("how-12wr-infographic");
    if (!img) {
        return;
    }

    img.alt =
        "The 12 Week Recomp System showing morning weigh-in, nutrition logging, workout tracking, the personalized prescription engine, and the daily dashboard prescription.";
    img.width = 1200;
    img.height = 900;

    const assetScript = document.createElement("script");
    assetScript.src = "/js/how-it-works-infographic-v2.js?v=2";
    assetScript.async = true;
    document.head.appendChild(assetScript);
})();
