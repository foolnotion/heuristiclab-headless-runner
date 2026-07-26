"""
Generate per-seed train/test draws for the Feynman-comparison benchmark
(Kronberger et al. 2021, arXiv:2103.15624), following the paper's own
methodology: 100 training + 100 test points sampled uniformly from each
problem's input domain (from the paper's supplementary Table 1), with an
optional noisy target y' = y + N(0, 0.05 * sigma_y).

sigma_y is computed from the noise-free target values over the combined
train+test draw for that seed (matches "for each of the data sets we
generated two versions: one without noise and one where we have added
normally distributed noise").

Usage: python gen_draws.py <problem_name> <out_dir> [--seeds N]
"""
import argparse
import math
import os
import random

PROBLEMS = {
    # Formula, domain and alpha_0=-2.0 taken verbatim from HeuristicLab's own
    # bundled instance generator (HeuristicLab.Problems.Instances.DataAnalysis/
    # 3.3/Regression/Physics/AircraftLift.cs), confirmed by back-solving alpha_0
    # from a reference draw (mean=2.0019, median=1.996 for C_L=CLalpha*(alpha+a0)+...,
    # i.e. a0=-2.0 in HL's (alpha - a0) convention). This paper's own supplementary
    # material's stated domain ([0.3..0.9] etc.) does NOT match the actual generator
    # used - HL's narrower domain (below) is authoritative.
    "aircraft_lift": {
        "vars": ["CLalpha", "alpha", "CLdelta_e", "delta_e", "S_HT", "S_ref"],
        "domain": [(0.4, 0.8), (5.0, 10.0), (0.4, 0.8), (5.0, 10.0), (1.0, 1.5), (5.0, 7.0)],
        "fn": lambda v: v["CLalpha"] * (v["alpha"] - (-2.0)) + v["CLdelta_e"] * v["delta_e"] * (v["S_HT"] / v["S_ref"]),
    },
}


def gen_rows(spec, n, rng):
    rows = []
    for _ in range(n):
        vals = {name: rng.uniform(lo, hi) for name, (lo, hi) in zip(spec["vars"], spec["domain"])}
        y = spec["fn"](vals)
        rows.append((vals, y))
    return rows


def write_csv(path, spec, rows, noisy, sigma_y):
    with open(path, "w") as f:
        f.write(",".join(spec["vars"]) + ",y\n")
        for vals, y in rows:
            yy = y + (random.gauss(0, 0.05 * sigma_y) if noisy else 0.0)
            f.write(",".join(str(vals[n]) for n in spec["vars"]) + "," + str(yy) + "\n")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("problem")
    ap.add_argument("out_dir")
    ap.add_argument("--seeds", type=int, default=30)
    ap.add_argument("--n_train", type=int, default=100)
    ap.add_argument("--n_test", type=int, default=100)
    args = ap.parse_args()

    spec = PROBLEMS[args.problem]
    os.makedirs(args.out_dir, exist_ok=True)

    for seed in range(1, args.seeds + 1):
        rng = random.Random(seed)
        train_rows = gen_rows(spec, args.n_train, rng)
        test_rows = gen_rows(spec, args.n_test, rng)

        all_y = [y for _, y in train_rows] + [y for _, y in test_rows]
        mean_y = sum(all_y) / len(all_y)
        sigma_y = math.sqrt(sum((y - mean_y) ** 2 for y in all_y) / len(all_y))

        random.seed(seed * 1000 + 1)
        write_csv(os.path.join(args.out_dir, f"seed{seed}_noise0_train.csv"), spec, train_rows, False, sigma_y)
        write_csv(os.path.join(args.out_dir, f"seed{seed}_noise0_test.csv"), spec, test_rows, False, sigma_y)
        random.seed(seed * 1000 + 2)
        write_csv(os.path.join(args.out_dir, f"seed{seed}_noise1_train.csv"), spec, train_rows, True, sigma_y)
        random.seed(seed * 1000 + 3)
        write_csv(os.path.join(args.out_dir, f"seed{seed}_noise1_test.csv"), spec, test_rows, True, sigma_y)

    print(f"Generated {args.seeds} seeds x (noise0/noise1) x (train/test) for {args.problem} in {args.out_dir}")


if __name__ == "__main__":
    main()
