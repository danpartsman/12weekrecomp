(() => {
    "use strict";

    const selector = "select[data-collapsible-workout-picker]";
    const ready = "data-collapsible-workout-picker-ready";
    const styleId = "collapsible-workout-picker-styles";

    function ensureStyles() {
        if (document.getElementById(styleId)) return;

        const style = document.createElement("style");
        style.id = styleId;
        style.textContent = `
            .cwp-native { display: none !important; }
            .cwp { position: relative; width: 100%; margin-top: .25rem; }
            .cwp-toggle {
                width: 100%; min-height: 2.65rem; display: flex; align-items: center;
                justify-content: space-between; gap: .75rem; text-align: left;
                padding: .7rem .8rem; border: 1px solid rgba(127,127,127,.45);
                border-radius: .45rem; background: var(--surface, #fff); color: inherit;
                font: inherit; cursor: pointer;
            }
            .cwp-toggle::after { content: "▾"; flex: 0 0 auto; }
            .cwp.is-open > .cwp-toggle::after { content: "▴"; }
            .cwp-panel {
                position: fixed; z-index: 60000; margin: 0; box-sizing: border-box;
                max-height: min(70dvh, 32rem); overflow: auto; overscroll-behavior: contain;
                padding: .45rem; border: 1px solid rgba(127,127,127,.45);
                border-radius: .5rem; background: #0b1f33; color: #d8eafa;
                box-shadow: 0 .65rem 1.6rem rgba(0,0,0,.18);
            }
            .cwp-panel[hidden], .cwp-options[hidden] { display: none !important; }
            .cwp-category + .cwp-category { margin-top: .35rem; }
            .cwp-category-button {
                width: 100%; display: flex; align-items: center; justify-content: space-between;
                gap: .75rem; padding: .68rem .72rem; border: 0; border-radius: .4rem;
                background: rgba(127,127,127,.10); color: inherit; font: inherit;
                font-weight: 700; text-align: left; cursor: pointer;
            }
            .cwp-category-button::after { content: "+"; flex: 0 0 auto; font-size: 1.1em; }
            .cwp-category-button[aria-expanded="true"]::after { content: "−"; }
            .cwp-count { margin-left: auto; margin-right: .35rem; font-size: .82em; font-weight: 500; opacity: .72; }
            .cwp-options { padding: .3rem 0 .15rem; }
            .cwp-option {
                width: 100%; display: block; padding: .62rem .75rem .62rem 1rem;
                border: 0; border-radius: .35rem; background: transparent; color: inherit;
                font: inherit; text-align: left; cursor: pointer;
            }
            .cwp-option:hover, .cwp-option:focus-visible, .cwp-option.is-selected {
                background: rgba(127,127,127,.14); outline: none;
            }
            .cwp-option.is-selected { font-weight: 700; }
        `;
        document.head.appendChild(style);
    }

    function positionPanel(shell) {
        const toggle = shell.querySelector(".cwp-toggle");
        const panel = shell.querySelector(".cwp-panel");
        if (panel.hidden) return;
        const r = toggle.getBoundingClientRect();
        const viewport = window.visualViewport;
        const height = viewport?.height || window.innerHeight;
        const width = viewport?.width || window.innerWidth;
        const panelWidth = Math.min(Math.max(r.width, 280), width - 24);
        panel.style.width = `${panelWidth}px`;
        panel.style.left = `${Math.max(12, Math.min(r.left, width - panelWidth - 12))}px`;
        const below = height - r.bottom - 12;
        const above = r.top - 12;
        const openAbove = below < 240 && above > below;
        panel.style.maxHeight = `${Math.max(100, Math.min(height * .70, openAbove ? above : below))}px`;
        panel.style.top = `${openAbove ? Math.max(12, r.top - panel.offsetHeight - 4) : Math.max(12, r.bottom + 4)}px`;
    }

    function closeCategory(category) {
        const button = category.querySelector(".cwp-category-button");
        const options = category.querySelector(".cwp-options");
        button?.setAttribute("aria-expanded", "false");
        if (options) options.hidden = true;
    }

    function closeCategories(shell) {
        shell.querySelectorAll(".cwp-category").forEach(closeCategory);
    }

    function closePicker(shell) {
        const panel = shell.querySelector(".cwp-panel");
        const toggle = shell.querySelector(".cwp-toggle");
        if (panel) { if (panel.matches(":popover-open")) panel.hidePopover(); panel.hidden = true; }
        shell.classList.remove("is-open");
        toggle?.setAttribute("aria-expanded", "false");
        closeCategories(shell);
    }

    function closeAll(except = null) {
        document.querySelectorAll(".cwp.is-open").forEach(shell => {
            if (shell !== except) closePicker(shell);
        });
    }

    function optionText(option) {
        return option?.textContent?.replace(/\s+/g, " ").trim() || "Choose a workout...";
    }

    function shellFor(select) {
        const shell = select.nextElementSibling;
        return shell?.classList.contains("cwp") ? shell : null;
    }

    function sync(select) {
        const shell = shellFor(select);
        if (!shell) return;
        const selected = select.options[select.selectedIndex];
        const toggle = shell.querySelector(".cwp-toggle");
        if (toggle) {
            toggle.disabled = select.matches(":disabled");
            toggle.textContent = selected?.value
                ? optionText(selected)
                : select.dataset.collapsiblePlaceholder || optionText(select.options[0]);
        }
        shell.querySelectorAll(".cwp-option").forEach(button => {
            button.classList.toggle("is-selected", button.dataset.value === select.value);
        });
    }

    function optionButton(select, option, shell) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "cwp-option";
        button.dataset.value = option.value;
        button.textContent = optionText(option);
        if (option.selected) button.classList.add("is-selected");

        button.addEventListener("click", event => {
            event.preventDefault();
            event.stopPropagation();
            if (select.matches(":disabled")) return;
            select.value = option.value;
            closePicker(shell);
            select.dispatchEvent(new Event("change", { bubbles: true }));
            window.setTimeout(() => sync(select), 0);
        });

        return button;
    }

    function addCategory(panel, select, shell, label, options) {
        if (!options.length) return;

        const category = document.createElement("section");
        category.className = "cwp-category";

        const heading = document.createElement("button");
        heading.type = "button";
        heading.className = "cwp-category-button";
        heading.setAttribute("aria-expanded", "false");

        const title = document.createElement("span");
        title.textContent = label;

        const count = document.createElement("span");
        count.className = "cwp-count";
        count.textContent = `${options.length}`;

        heading.appendChild(title);
        heading.appendChild(count);

        const list = document.createElement("div");
        list.className = "cwp-options";
        list.hidden = true;
        options.forEach(option => list.appendChild(optionButton(select, option, shell)));

        heading.addEventListener("click", event => {
            event.preventDefault();
            event.stopPropagation();

            const wasOpen = heading.getAttribute("aria-expanded") === "true";
            closeCategories(shell);

            if (!wasOpen) {
                heading.setAttribute("aria-expanded", "true");
                list.hidden = false;
                positionPanel(shell);
            }
        });

        category.appendChild(heading);
        category.appendChild(list);
        panel.appendChild(category);
    }

    function enhance(select) {
        if (!select || select.hasAttribute(ready) || select.matches(":disabled")) return;
        select.setAttribute(ready, "true");
        ensureStyles();

        const shell = document.createElement("div");
        shell.className = "cwp";

        const toggle = document.createElement("button");
        toggle.type = "button";
        toggle.className = "cwp-toggle";
        toggle.setAttribute("aria-haspopup", "listbox");
        toggle.setAttribute("aria-expanded", "false");

        const panel = document.createElement("div");
        panel.className = "cwp-panel";
        panel.hidden = true;
        panel.setAttribute("popover", "manual");

        const placeholder = Array.from(select.options).find(option => !option.value);
        select.dataset.collapsiblePlaceholder = optionText(placeholder);

        Array.from(select.children)
            .filter(child => child.tagName === "OPTGROUP")
            .forEach(group => {
                addCategory(
                    panel,
                    select,
                    shell,
                    group.label,
                    Array.from(group.children).filter(option => option.tagName === "OPTION")
                );
            });

        const direct = Array.from(select.children)
            .filter(child => child.tagName === "OPTION" && child.value);

        addCategory(
            panel,
            select,
            shell,
            "Current / Manual",
            direct.filter(option => !option.value.startsWith("__"))
        );

        addCategory(
            panel,
            select,
            shell,
            "My Workouts & Custom",
            direct.filter(option => option.value.startsWith("__"))
        );

        toggle.addEventListener("click", event => {
            event.preventDefault();
            event.stopPropagation();

            const opening = panel.hidden;
            closeAll(shell);
            closeCategories(shell); // every category is collapsed each time the dropdown opens
            panel.hidden = !opening;
            shell.classList.toggle("is-open", opening);
            toggle.setAttribute("aria-expanded", opening ? "true" : "false");
            if (opening) { panel.scrollTop = 0; panel.showPopover?.(); positionPanel(shell); }
            else closePicker(shell);
        });

        panel.addEventListener("click", event => event.stopPropagation());

        select.classList.add("cwp-native");
        select.tabIndex = -1;
        select.setAttribute("aria-hidden", "true");
        select.insertAdjacentElement("afterend", shell);
        shell.appendChild(toggle);
        shell.appendChild(panel);

        sync(select);
        select.addEventListener("change", () => window.setTimeout(() => sync(select), 0));
    }

    function enhanceAll(root = document) {
        if (root instanceof Element && root.matches(selector)) enhance(root);
        root.querySelectorAll?.(selector).forEach(enhance);
    }

    document.addEventListener("click", event => {
        if (!event.target.closest(".cwp")) closeAll();
    });
    window.addEventListener("resize", () => document.querySelectorAll(".cwp.is-open").forEach(positionPanel));
    window.addEventListener("scroll", event => {
        if (!event.target.closest?.(".cwp-panel")) document.querySelectorAll(".cwp.is-open").forEach(positionPanel);
    }, true);
    document.addEventListener("keydown", event => {
        if (event.key === "Escape") closeAll();
    });

    enhanceAll();

    new MutationObserver(mutations => {
        mutations.forEach(mutation => {
            mutation.addedNodes.forEach(node => {
                if (node instanceof Element) enhanceAll(node);
            });
        });
    }).observe(document.body, { childList: true, subtree: true });
})();
