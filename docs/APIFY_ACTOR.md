# Apify price-check actor

Deploy `apify/pricing-actor/` with the Apify CLI: install the CLI if needed, run `apify login` using the owner's own Apify token, then from that directory run `apify push`. Do not put the token in source control.

Set the backend environment variable `APIFY_PRICING_ACTOR` to the deployed actor ID and `APIFY_TOKEN` to the owner's private Apify API token. The backend invokes the actor's synchronous dataset endpoint; its output rows must preserve the `item`, `usd`, `source_url`, and `retrieved_at` contract.

**not deployed, not verified live**
