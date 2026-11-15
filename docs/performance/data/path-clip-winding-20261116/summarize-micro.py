import json, pathlib
r=pathlib.Path('/home/hideaki/.cache/pcw-evidence')
runs=[]
for name in ['A1','B1','B2','A2']:
 p=list((r/'micro'/name).rglob('ClipCpuBenchmarks-report-full.json'))
 assert len(p)==1
 d=json.load(open(p[0])); assert len(d['Benchmarks'])==4
 cases=[]
 for b in d['Benchmarks']:
  s=b['Statistics']; rows=[x for x in b['Measurements'] if x['IterationMode']=='Workload' and x['IterationStage']=='Actual']
  cases.append(dict(case=b['Parameters'],medianNs=s['Median'],p95Ns=s['Percentiles']['P95'],allocatedBytes=b['Memory']['BytesAllocatedPerOperation'],iterations=s['N'],confidence999FullWidth=2*s['ConfidenceInterval']['Margin']/s['Mean'],minimumIterationMs=min(x['Nanoseconds']/1e6 for x in rows),allIterationEstimatesNs=s['OriginalValues'],outliersRetained=s['AllOutliers']))
 runs.append(dict(name=name,cases=cases))
json.dump(dict(boundary='cold CPU only; fresh prep cache inside each invocation; no GPU; delegate bindings setup-only; recurring states do not retain masks',runs=runs),open(r/'micro-summary.json','w'),indent=2)
