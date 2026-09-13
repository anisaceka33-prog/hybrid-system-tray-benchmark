"""Descriptive analysis only; raw CSV files are never modified."""
import argparse, csv, glob, json, math, statistics
from pathlib import Path
REQUIRED={'run_id','session_id','timestamp','variant','lifecycle','payload_bytes','direction','metric','value','unit','cycle_index','status','failure_reason','build_commit','runtime_version'}
def main():
    p=argparse.ArgumentParser(); p.add_argument('path',nargs='+',default=['results/raw/*.csv']); p.add_argument('--out',default='results/processed'); a=p.parse_args(); rows=[]; errors=[]
    for pattern in a.path:
        for file in glob.glob(pattern):
            with open(file,newline='',encoding='utf-8') as f:
                reader=csv.DictReader(f); missing=REQUIRED-set(reader.fieldnames or [])
                if missing: errors.append({'file':file,'missing':sorted(missing)}); continue
                for r in reader:
                    try: r['value']=float(r['value']); r['payload_bytes']=int(r['payload_bytes'])
                    except ValueError: errors.append({'file':file,'error':'non-numeric value'}); continue
                    if r['value']<0: errors.append({'file':file,'error':'negative value','run_id':r['run_id']})
                    rows.append(r)
    groups={}
    for r in rows: groups.setdefault((r['variant'],r['lifecycle'],r['metric'],r['payload_bytes']),[]).append(r['value'])
    summaries=[]
    for (variant,lifecycle,metric,payload),values in sorted(groups.items()):
        values.sort(); n=len(values); mean=statistics.fmean(values); sd=statistics.stdev(values) if n>1 else 0.0; q1=values[(n-1)//4]; q3=values[(3*(n-1))//4]
        summaries.append({'variant':variant,'lifecycle':lifecycle,'metric':metric,'payload_bytes':payload,'n':n,'mean':mean,'median':statistics.median(values),'stddev':sd,'iqr':q3-q1,'min':values[0],'max':values[-1],'ci95':1.96*sd/math.sqrt(n) if n>1 else 0.0,'p95':values[min(n-1,math.ceil(.95*n)-1)]})
    out=Path(a.out); out.mkdir(parents=True,exist_ok=True); (out/'validation.json').write_text(json.dumps({'rows':len(rows),'errors':errors},indent=2)); (out/'summary.json').write_text(json.dumps(summaries,indent=2)); print(json.dumps({'rows':len(rows),'errors':len(errors),'groups':len(summaries)},indent=2))
if __name__=='__main__': main()
