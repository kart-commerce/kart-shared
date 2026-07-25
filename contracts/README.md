# Contracts

Synced copies of every approved service's `api-contract.yaml` and `event-contract.md`, one
subfolder per service. This is what PLATFORM_BLUEPRINT.md means by kart-shared holding "versioned
OpenAPI contracts": `kart-platform/docs/services/<name>/` is the design record of record (where
these are authored, reviewed, and marked `status: approved`), and this folder is the package
consumers actually pull — a `kart-web`/`kart-admin-web` OpenAPI client generator, another
service's contract-test suite, a downstream integration, etc.

**These files are regenerated from `kart-platform`, never hand-edited here.** If a contract needs
to change, edit it in `kart-platform/docs/services/<name>/` through the normal
requirement → architecture → DDD → API/event design pipeline (see
`docs/PLATFORM_BLUEPRINT.md` §8.2), get it re-approved, then re-copy it here. A hand-edit made
directly in this folder will be silently overwritten (and worse, will have skipped the Contract
Compatibility Agent gate kart-conventions.md's API Versioning section requires for every
version bump) the next time someone regenerates this folder — treat it as read-only.

## Current snapshot

18 services, each with both files present and `status: approved` as of the date these were last
synced (2026-07-25): `kart-admin-service`, `kart-analytics-service`, `kart-cart-service`,
`kart-category-service`, `kart-delivery-tracking-service`, `kart-identity-service`,
`kart-inventory-service`, `kart-notification-service`, `kart-offer-service`, `kart-order-service`,
`kart-payment-service`, `kart-product-service`, `kart-recommendation-service`,
`kart-review-service`, `kart-search-service`, `kart-shipping-service`, `kart-user-service`,
`kart-wishlist-service`.

`kart-api-gateway` is intentionally excluded: it has an `api-contract.yaml` in
`kart-platform/docs/services/` but no `event-contract.md` (it's a routing layer, not a domain
service that publishes events), so it doesn't meet the "both files present and approved" bar this
folder syncs against.

## Regenerating

There is no automated sync job yet (that's a `kart-devops` reusable-workflow candidate, not
something this repo runs itself). Until then, regenerate by hand:

```bash
for svc in docs/services/*/; do
  name=$(basename "$svc")
  [ -f "$svc/api-contract.yaml" ] && [ -f "$svc/event-contract.md" ] || continue
  grep -q "status: approved" "$svc/api-contract.yaml" || continue
  grep -q "status: approved" "$svc/event-contract.md" || continue
  mkdir -p "kart-shared/contracts/$name"
  cp "$svc/api-contract.yaml" "kart-shared/contracts/$name/api-contract.yaml"
  cp "$svc/event-contract.md" "kart-shared/contracts/$name/event-contract.md"
done
```

(run from `kart-platform`'s repo root, with `kart-shared` checked out as a sibling directory)
