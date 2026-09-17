# 12WR TEST v1012 — B3.1 Global Design System

## Scope

B3.1 establishes the shared visual foundation for the v1012 redesign. It is intentionally presentation-only and does not change controller actions, Razor model binding, validation, antiforgery, authorization, SQL behavior, Engine 3.0 calculations, or workout/nutrition business logic.

## Design direction

The v1012 application shell uses a near-black performance-tech visual system with restrained luminous accents:

- Neon green: primary action / success
- Cyan / electric blue: data / information / training
- Purple / magenta: training / progress specialization
- Amber: caution / recovery / fueling
- Red: error / destructive only
- Dark neutral surfaces: dominant application background and card system

Typography uses a condensed display stack for high-impact headings and the system UI sans stack for body content and controls. The implementation does not require downloadable font assets.

## Token foundation

The design system introduces reusable CSS custom properties for:

- Background and surface hierarchy
- Border hierarchy
- Primary and muted text
- Semantic accent colors
- Radius scale
- Spacing scale
- Shadow/elevation levels
- Display and body font stacks

The existing TEST visual variables are bridged to these tokens so existing pages can inherit the new direction before page-by-page component conversion.

## Shared component primitives

The B3.1 stylesheet defines reusable primitives for later phases:

- `ds-page-hero`
- `ds-card`
- `ds-metric-card`
- `ds-chart-panel`
- `ds-button-primary`
- `ds-button-secondary`
- `ds-button-danger`
- `ds-field`
- `ds-field-label`
- `ds-table`
- `ds-status-chip`
- `ds-filter-chip`
- `ds-section-heading`
- `ds-image-rail`
- `ds-sticky-mobile-action`

Existing application classes are also mapped into the new visual foundation so B3.1 can improve consistency without changing Razor page structure.

## Responsive foundation

B3.1 establishes application-shell behavior around the project breakpoints:

- Desktop / wide: up to a 1440px content frame
- Tablet: 1024px transition rules
- Mobile navigation: 820px and below
- Narrow mobile: 480px refinement
- Form controls maintain at least 44px interaction height
- Mobile form text remains 16px to reduce browser zoom behavior
- Reduced-motion preference is respected

## TEST-only integration

The new stylesheet is:

`src/WebRecomp.Web/wwwroot/css/design-system-v1012.css`

It is loaded by the existing TEST-only `test-live-vibrance.js` staging script after the legacy TEST preview CSS. This lets the B3.1 layer override the older preview rules while preserving the existing application markup and logic.

Public and onboarding pages are not deliberately converted in B3.1; their redesign remains part of B3.2. The primary B3.1 target is the authenticated TEST application shell and reusable component foundation.

## Safety boundary

B3.1 must remain visual-only. Any later phase that requires markup restructuring should preserve existing form names, inputs, action URLs, POST boundaries, antiforgery tokens, model binding, validation hooks, IDs/classes used by JavaScript, and Engine/data integration unless a separate functional change is explicitly approved.

## TEST deployment checkpoint

B3.1 was fast-forwarded from `design/b3-1-global-system` into `test`. This documentation-only follow-up commit exists to trigger the normal TEST build/test/deploy workflow so the design foundation is validated through the same deployment path as other TEST changes.

## Next phase

B3.2: public experience redesign, followed by B3.3 dashboard/daily application surfaces.
