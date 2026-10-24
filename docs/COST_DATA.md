# Cost model: real data sources

The cost estimator turns measured performance into dollars using published figures. Every rate below comes from a published source and must be shown on screen with its citation. Results are labeled **illustrative estimates, not billing data**.

> Verification status: figures below were taken from search-result summaries of each paper. Before the demo, open each source and confirm the number, year and population.

## Rates

| Input | Value | Source |
|---|---|---|
| OR time, inpatient | **$37.45/min** (SD 16.04) | Childers & Maggard-Gibbons, *JAMA Surgery* 2018;153(4):e176233. California hospitals, FY2014. https://jamanetwork.com/journals/jamasurgery/fullarticle/2673385 |
| OR time, ambulatory | **$36.14/min** (SD 19.53) | same |
| OR time, direct cost share | $20-21/min direct; $13-14 wages and benefits; $2.50-3.50 nonbillable supplies | same |
| OR time, cross-study mean | **$46.04/min** (SD 32.31), 14 published estimates | *J Orthopaedic Business*, "Cost of OR Time is $46.04 per Minute". https://jorthobusiness.org/index.php/jorthobusiness/article/view/23 |
| Reusable laparoscopic instruments | ~$47-51 per case | Apelgren et al. 1994. https://pubmed.ncbi.nlm.nih.gov/8153862/ |
| Disposable laparoscopic instruments | ~$330-460 per case (range $385-515 full set) | same |
| Reusable vs disposable, modern review | Reusables cut cost $16-388 per case | *Surg Endosc* systematic review, 4,724 cases. https://link.springer.com/article/10.1007/s00464-026-12765-x |
| Intraoperative adverse event, abdominal surgery | Median charges $27,169 with vs $13,312 without; +41% adjusted | "The financial impact of intraoperative adverse events in abdominal surgery". https://pubmed.ncbi.nlm.nih.gov/26026793/ |
| Postoperative complication, general surgery | +$19,626 mean hospital cost | *JAMA Surgery*, "Hospital and Payer Costs Associated With Surgical Complications". https://jamanetwork.com/journals/jamasurgery/fullarticle/2519879 |
| Postoperative complication, Medicare | +$23,199 cost, +$18,022 payment | CER 2023. https://becarispublishing.com/doi/10.57264/cer-2023-0080 |

## How performance maps to cost

1. **Time cost** = elapsed minutes x OR rate. Default $37.45/min, with the $46.04/min cross-study mean as a toggle. Real, sourced.
2. **Instrument cost** = reusable (~$47-51) or disposable (~$330-460) per case, selectable. Real, sourced (1994 dollars; apply a stated inflation adjustment or label as historical).
3. **Error cost** = expected cost of an adverse event x an assumed per-error event probability.
   - The *cost of an event* is real: roughly +$13,857 (the $27,169 - $13,312 median charge difference) for an intraoperative adverse event, or +$19,626 for a complication.
   - The *probability that a simulated drop or invalid release becomes a real adverse event* is **not in any dataset**. It is an explicit, adjustable assumption, shown on screen as "assumed risk per error".
4. **Inefficiency cost** = extra minutes implied by low path efficiency, priced at the OR rate. Extra minutes = actual time minus time at ideal path efficiency, so it reuses (1).

Only (4) and the probability in (3) are modeling assumptions. Everything else is a cited number.

## Known gaps

- OR cost is California FY2014 and excludes surgeon professional fees.
- Instrument data is from 1994-95 audits plus one recent review.
- Adverse event charges are charges, not costs, in at least one source.
- No source links simulator performance to real-world outcomes. The demo should say "what this would cost if it happened in an operating room", not predict outcomes.
