#!/usr/bin/env python3
"""WBPP exposure audit by observing session/filter. Python 3.9+, no dependencies.

Usage:
  python wbpp_exposure_report.py "C:\\path\\to\\WBPP\\logs"
  python wbpp_exposure_report.py "C:\\path\\to\\WBPP\\logs" --output exposure.tsv
  python wbpp_exposure_report.py "C:\\path\\to\\WBPP\\logs" --all-runs

By default uses the newest completed WBPP .log in the directory. With --all-runs,
prints separate reports for each log, never adds reprocessed frames together.
"""
import argparse
import collections
import csv
import io
import re
import sys
from pathlib import Path

STAMP = re.compile(r'^\[\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\] ?')
GROUP = re.compile(r'^Group of (\d+) Light frames \((\d+) active\)')
FRAME = re.compile(r'^\[\d{3}\]\s+(.+?\.(?:fits?|xisf))\s*$', re.I)
EXCLUDED = re.compile(r'^(.*?)\s+--\s+(.+)$')
SESSION_FOLDER = re.compile(r'[/\\]NIGHT_\d+_(\d{4}-\d{2}-\d{2})_LIGHT[/\\]', re.I)
DATE_IN_NAME = re.compile(r'^(\d{4}-\d{2}-\d{2})_\d{2}-\d{2}-\d{2}_')


def normalize(path):
    return path.replace('\\', '/').casefold()


def session_date(path):
    match = SESSION_FOLDER.search(path)
    if match:
        return match.group(1)
    match = DATE_IN_NAME.search(path.replace('\\', '/').rsplit('/', 1)[-1])
    return match.group(1) if match else 'Unknown'


def duration(seconds):
    mins = round(seconds / 60)
    if abs(seconds / 60 - mins) < 1e-8:
        return f'{mins // 60:02d}:{mins % 60:02d}'
    hours, rem = divmod(round(seconds), 3600)
    mm, ss = divmod(rem, 60)
    return f'{hours:02d}:{mm:02d}:{ss:02d}'


def filter_sort(name):
    order = {'L': 0, 'R': 1, 'G': 2, 'B': 3, 'HA': 4, 'OIII': 5, 'SII': 6}
    return (order.get(name.upper(), 100), name)


def parse_log(path):
    records = {}  # source path -> {session,filter,seconds,rejection}
    integration_expected = collections.Counter()
    in_calibration = False
    in_integration = False
    collecting_excluded = False
    this_filter = None
    this_exposure = None
    pending_group = 0
    seen_group = 0
    completed = False

    with path.open('r', encoding='utf-8-sig', errors='replace') as handle:
        for raw in handle:
            line = STAMP.sub('', raw.rstrip('\r\n'))
            if line == '* Begin calibration of Light frames':
                in_calibration = True
                in_integration = False
                this_filter = None
                this_exposure = None
                pending_group = 0
                seen_group = 0
                continue
            if line == '* End calibration of Light frames':
                if pending_group and seen_group != pending_group:
                    print(f'WARNING: calibration group has {seen_group} listed frames, expected {pending_group}', file=sys.stderr)
                in_calibration = False
                continue
            if line == '* Begin integration of Light frames':
                in_integration = True
                in_calibration = False
                this_filter = None
                this_exposure = None
                continue
            if line == '* End integration of light frames' or line == '* End integration of Light frames':
                in_integration = False
                continue

            if in_calibration or in_integration:
                m = GROUP.match(line)
                if m:
                    pending_group = int(m.group(1))
                    seen_group = 0
                elif line.startswith('Filter   :'):
                    this_filter = line.split(':', 1)[1].strip()
                elif line.startswith('Exposure :'):
                    try:
                        this_exposure = float(line.split(':', 1)[1].strip().removesuffix('s'))
                    except ValueError:
                        this_exposure = None

                if in_calibration:
                    m = FRAME.match(line)
                    if m and this_filter is not None and this_exposure is not None:
                        source = m.group(1)
                        key = normalize(source)
                        records[key] = dict(source=source, session=session_date(source),
                                            filter=this_filter, seconds=this_exposure,
                                            rejected=False, reason='')
                        seen_group += 1
                elif in_integration and line.startswith('===== ImageIntegration:'):
                    # Multiple such lines could be logged per group; use the first main
                    # integration count and cross-check only on group completion if available.
                    m = re.search(r'ImageIntegration:\s*(\d+) succeeded', line)
                    if m and this_filter is not None:
                        integration_expected[this_filter] = int(m.group(1))

            if 'frame(s) excluded from the execution:' in line:
                collecting_excluded = True
                continue
            if collecting_excluded:
                m = EXCLUDED.match(line)
                if m:
                    key = normalize(m.group(1))
                    if key in records:
                        records[key]['rejected'] = True
                        records[key]['reason'] = m.group(2)
                    else:
                        print(f'WARNING: excluded file not found among initial frames: {m.group(1)}', file=sys.stderr)
                elif not line.strip() or line.startswith('***'):
                    collecting_excluded = False

            if line.startswith('Execution summary'):
                completed = True

    if not records:
        raise ValueError('No original calibration light-frame file lists found in log')
    return list(records.values()), integration_expected, completed


def create_rows(records):
    groups = collections.defaultdict(list)
    for r in records:
        groups[(r['session'], r['filter'])].append(r)

    def summary(items, label, filt):
        start = len(items)
        rejected = sum(r['rejected'] for r in items)
        used = start - rejected
        exposure_values = sorted({r['seconds'] for r in items})
        exp_str = '/'.join(f'{x:g}' for x in exposure_values)
        start_secs = sum(r['seconds'] for r in items)
        used_secs = sum(r['seconds'] for r in items if not r['rejected'])
        return [label, filt, start, rejected, used, exp_str,
                duration(start_secs), duration(start_secs-used_secs), duration(used_secs)]

    rows = []
    for session in sorted({date for date, _ in groups}):
        for filt in sorted((f for date, f in groups if date == session), key=filter_sort):
            rows.append(summary(groups[(session, filt)], session, filt))
        rows.append(summary([r for r in records if r['session'] == session], session, 'SESSION TOTAL'))
    rows.append(['', '', '', '', '', '', '', '', ''])
    for filt in sorted({r['filter'] for r in records}, key=filter_sort):
        rows.append(summary([r for r in records if r['filter'] == filt], 'ALL SESSIONS', filt))
    rows.append(summary(records, 'ALL SESSIONS', 'GRAND TOTAL'))
    return rows


def get_logs(argument, all_runs):
    path = Path(argument).expanduser()
    if path.is_file():
        return [path]
    if not path.is_dir():
        raise FileNotFoundError(f'Not a file or directory: {path}')
    logs = sorted(path.glob('*.log'), key=lambda f: f.stat().st_mtime, reverse=True)
    if not logs:
        raise FileNotFoundError(f'No .log files found in {path}')
    return logs if all_runs else logs[:1]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('path', nargs='?', default='.', help='WBPP logs directory or a specific .log')
    ap.add_argument('--output', '-o', help='Write TSV file instead of printing it')
    ap.add_argument('--all-runs', action='store_true', help='Report each log separately (no summing across runs)')
    args = ap.parse_args()

    results = []
    logs = get_logs(args.path, args.all_runs)
    for log in logs:
        try:
            records, expected, completed = parse_log(log)
        except ValueError as e:
            print(f'SKIP {log.name}: {e}', file=sys.stderr)
            continue
        if not completed:
            print(f'WARNING: {log.name} appears incomplete; used counts may be provisional', file=sys.stderr)
        actual = collections.Counter(r['filter'] for r in records if not r['rejected'])
        for filt, count in expected.items():
            if actual[filt] != count:
                print(f'WARNING: {log.name}: {filt} used={actual[filt]} but integration succeeded={count}', file=sys.stderr)
        output = io.StringIO()
        writer = csv.writer(output, delimiter='\t', lineterminator='\n')
        writer.writerow(['Session date', 'Filter', 'Starting subs', 'Rejected subs',
                         'Used subs', 'Exposure/sub (s)', 'Captured (hh:mm)',
                         'Rejected (hh:mm)', 'Integrated (hh:mm)'])
        writer.writerows(create_rows(records))
        results.append((log, output.getvalue()))

    if not results:
        sys.exit('No readable WBPP calibration logs found.')
    if args.output:
        with open(args.output, 'w', encoding='utf-8-sig', newline='') as output:
            for i, (log, report) in enumerate(results):
                if i:
                    output.write('\n')
                if len(results) > 1:
                    output.write(f'Log: {log.name}\n')
                output.write(report)
        print(f'Wrote {args.output} ({len(results)} WBPP run(s))')
    else:
        for i, (log, report) in enumerate(results):
            if i:
                print()
            print(f'# WBPP run: {log.name}')
            print(report, end='')


if __name__ == '__main__':
    main()
