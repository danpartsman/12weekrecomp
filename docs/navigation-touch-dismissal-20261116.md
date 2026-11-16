# Responsive menu dismissal

The destination-page flyout still opens after choosing a primary section and dismisses after five seconds. Touch-generated hover and retained link focus no longer suspend that timeout indefinitely. Real mouse hover and intentional keyboard navigation continue to hold an active submenu open.

Choosing Command Center or another navigation link immediately dismisses the old menu while the destination loads. Direct mobile links also dismiss the hamburger menu. Returning through the browser's back/forward cache, or leaving the page in a background tab, clears stale open-menu state.

Eight DOM regression tests cover destination routing, sticky touch hover/focus, real mouse hover, keyboard interaction, switching input modes, immediate primary/direct navigation feedback, and cached-page restoration. The desktop/mobile browser check now also uses a real touch-enabled mobile context to verify automatic dismissal. No Command Center calculations or saved workout data change.
