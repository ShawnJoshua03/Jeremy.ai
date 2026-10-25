# Cost model: real data sources

The cost estimator turns measured performance into dollars using published figures. Every rate below comes from a published source and must be shown on screen with its citation. Results are labeled **illustrative estimates, not billing data**.

> Source check on 2026-10-24. Each citation was opened (publisher page, PubMed abstract, or the results paragraph shown on that page). Paywalled full texts were not purchased. The Source check section lists every figure the source does not support. Earlier drafts used search summaries; those mismatches are corrected in the table.

## Rates

| Input | Value | Source |
|---|---|---|
| OR time, inpatient | **$37.45/min** (SD 16.04) | Childers & Maggard-Gibbons, *JAMA Surgery* 2018;153(4):e176233. California hospitals, FY2014. https://jamanetwork.com/journals/jamasurgery/fullarticle/2673385 |
| OR time, ambulatory | **$36.14/min** (SD 19.53) | same |
| OR time, direct cost share | $20-21/min direct; $13-14 wages and benefits; $2.50-3.50 nonbillable supplies | same |
| OR time, cross-study mean | **$46.04/min** (SD 32.31), mean of 14 published estimates, inflation-adjusted to 2022 dollars | Smith et al., *J Orthopaedic Business* 2022, "The Cost of OR Time is $46.04 per Minute". https://jorthobusiness.org/index.php/jorthobusiness/article/view/23 |
| Reusable laparoscopic instruments | **$46.92–$50.67 per case** (1994 dollars; code uses the midpoint $48.80) | Apelgren et al. 1994. https://pubmed.ncbi.nlm.nih.gov/8153862/ |
| Disposable laparoscopic instruments | **$330.00–$460.00 per case** (1994 dollars; code uses the midpoint $395) | same |
| Reusable vs disposable, modern review | In general surgery, reusables saved **$16–$388 per procedure**. The review includes **4,724 procedures**; that savings range is the general-surgery subset, not the result for every specialty | *Surg Endosc* 2026 systematic review. https://link.springer.com/article/10.1007/s00464-026-12765-x |
| Intraoperative adverse event, abdominal surgery | Median charges $27,169 with vs $13,312 without; +41% adjusted | "The financial impact of intraoperative adverse events in abdominal surgery". https://pubmed.ncbi.nlm.nih.gov/26026793/ |
| Postoperative complication, general surgery | +$19,626 mean hospital cost | *JAMA Surgery*, "Hospital and Payer Costs Associated With Surgical Complications". https://jamanetwork.com/journals/jamasurgery/fullarticle/2519879 |
| Postoperative complication, Medicare | **+$23,199** estimated provider cost ($39,357 with vs $16,158 without). Reported reimbursement means differ by **$18,023** ($34,664 vs $16,641). The abstract does not state +$18,022 | Haidar, Vazquez & Medic, *J Comp Eff Res* 2023. https://becarispublishing.com/doi/10.57264/cer-2023-0080 |

## How performance maps to cost

1. **Time cost** = elapsed minutes x OR rate. Default $37.45/min, with the $46.04/min cross-study mean as a toggle. Real, sourced.
2. **Instrument cost** = reusable ($46.92–$50.67; code uses $48.80) or disposable ($330–$460; code uses $395) per case, selectable. Real, sourced (1994 dollars; apply a stated inflation adjustment or label as historical).
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

## Source check (2026-10-24)

Supported by the page that was opened:

- Childers & Maggard-Gibbons, *JAMA Surgery* 2018;153(4):e176233. Inpatient **$37.45** (SD **$16.04**) and ambulatory **$36.14** (SD **$19.53**) per minute, California hospitals, FY2014. The direct-cost row matches the paper's Key Points: $20–$21 direct, $13–$14 wages and benefits, $2.50–$3.50 nonbillable supplies. The results paragraph also prints tighter figures ($20.40 and $20.90 direct; $14.00 and $14.35 wages; $2.55 and $3.33 supplies).
- Smith et al., *Journal of Orthopaedic Business* 2022. Fourteen novel estimates, mean **$46.04**, SD **$32.31**, described in the paper as inflation-adjusted to 2022 dollars.
- Apelgren et al., *Surgical Endoscopy* 1994 (PMID 8153862). Reusable **$46.92–$50.67** per case and disposable **$330.00–$460.00** per case. `backend/cost.py` midpoints $48.80 and $395 sit inside those ranges.
- Ramly et al., *Surgery* 2015 (PMID 26026793). Median total **charges** $27,169 with an intraoperative adverse event vs $13,312 without, and an adjusted **+41%**. The estimator's $13,857 is $27,169 − $13,312. These are charges, not costs.
- *JAMA Surgery* 2016, "Hospital and Payer Costs Associated With Surgical Complications" (the cited URL; PMID 27168356). Mean hospital costs **$19,626** higher with complications ($36,060 vs $16,434), one health system.

The source does **not** support these earlier wordings:

| What the doc used to say | What the opened source actually says |
|---|---|
| Reusable instruments "~$47–51 per case" | $46.92–$50.67. The rounded bounds are not the published range. |
| Disposable instruments "(range $385–515 full set)" | Not in the Apelgren abstract. The published disposable range is $330.00–$460.00. |
| Reusables "cut cost $16–388 per case" across "4,724 cases" | The review does report $16–$388 and 4,724 procedures. The dollar range is the general-surgery subset. It does not support that savings figure for all 4,724 procedures. In gynecology, thoracic, and spinal series in the same review, disposables were sometimes associated with lower total cost. |
| Medicare complications "+$18,022 reimbursement" | The abstract's reported means ($34,664 vs $16,641) differ by **$18,023**. The abstract does not state +$18,022. The cost difference +$23,199 does match $39,357 − $16,158. |
