"""
Aggregate raw per-run HeadlessRunner results (problem,noise,variant,seed,
train_nmse_pct,test_nmse_pct,generations,elapsed_seconds) into a per-cell
summary: median train/test NMSE (%) over the 30 seeds for each
(problem, noise, variant) combination.

Usage: python summarize_results.py <results_dir_or_glob> <out_csv>
  results_dir_or_glob: either a directory containing *_results.csv files,
  or a single combined CSV (e.g. full_results.csv).
"""
import argparse
import csv
import glob
import os
import statistics


def load_rows(path):
    rows = []
    if os.path.isdir(path):
        files = glob.glob(os.path.join(path, "*_results.csv"))
    else:
        files = [path]
    for f in files:
        with open(f, newline="") as fh:
            rows.extend(csv.DictReader(fh))
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("results")
    ap.add_argument("out_csv")
    args = ap.parse_args()

    rows = load_rows(args.results)
    cells = {}
    for r in rows:
        key = (r["problem"], r["noise"], r["variant"])
        cells.setdefault(key, {"train": [], "test": [], "elapsed": []})
        cells[key]["train"].append(float(r["train_nmse_pct"]))
        cells[key]["test"].append(float(r["test_nmse_pct"]))
        cells[key]["elapsed"].append(float(r["elapsed_seconds"]))

    with open(args.out_csv, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["problem", "noise", "variant", "n_seeds",
                    "median_train_nmse_pct", "median_test_nmse_pct",
                    "mean_train_nmse_pct", "mean_test_nmse_pct",
                    "median_elapsed_seconds"])
        for (problem, noise, variant) in sorted(cells):
            c = cells[(problem, noise, variant)]
            w.writerow([
                problem, noise, variant, len(c["train"]),
                f"{statistics.median(c['train']):.6f}",
                f"{statistics.median(c['test']):.6f}",
                f"{statistics.mean(c['train']):.6f}",
                f"{statistics.mean(c['test']):.6f}",
                f"{statistics.median(c['elapsed']):.2f}",
            ])

    print(f"Wrote {len(cells)} (problem,noise,variant) cells to {args.out_csv}")


if __name__ == "__main__":
    main()
