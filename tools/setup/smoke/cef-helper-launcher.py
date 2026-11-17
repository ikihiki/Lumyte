#!/usr/bin/env python3
"""Opt-in launcher checks; no host desktop, no CEF flags, no service edits.
Requires --weston-root pointing to an already extracted Weston dependency tree.
Writes only a fresh --out directory and owned temporary roots. Never downloads.
"""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import resource
import select
import shutil
import signal
import struct
import subprocess as sp
import tempfile
import time

REPO = Path(__file__).resolve().parents[3]
FIXTURE = r'''
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <signal.h>
#include <unistd.h>
#include <fcntl.h>
#include <sys/wait.h>
int main(int argc, char **argv) {
 if(argc == 2 && !strcmp(argv[1], "--type=fixture")) {
  char name[4096], exe[4096];
  snprintf(name,sizeof(name),"%s.child",getenv("FIXTURE_PROOF"));
  ssize_t n=readlink("/proc/self/exe",exe,sizeof(exe)-1); if(n<0) return 94; exe[n]=0;
  FILE *f=fopen(name,"w"); if(!f) return 95;
  fprintf(f,"%s\n%s\n",exe,getenv("DBUS_SESSION_BUS_ADDRESS"));fclose(f);
  return fcntl(8,F_GETFD)==-1 && fcntl(9,F_GETFD)==-1 ? 0 : 96;
 }
 if((argc != 2 && argc != 3) || strcmp(argv[1], "--lumyte-helper") || (argc == 3 && strcmp(argv[2], "--lumyte-allow-unsandboxed"))) return 90;
 FILE *p = fopen(getenv("FIXTURE_PROOF"), "w");
 if(!p) return 91;
 fprintf(p,"%ld\n%s\n%s\n",(long)getpid(),getenv("DBUS_SESSION_BUS_ADDRESS"),getenv("XDG_RUNTIME_DIR")); fclose(p);
 pid_t child=fork(); if(child<0) return 97;
 if(child==0) { execl("/proc/self/exe","lumyte-cef-helper","--type=fixture",NULL); _exit(98); }
 int status; if(waitpid(child,&status,0)<0 || !WIFEXITED(status) || WEXITSTATUS(status)) return 99;
 uint32_t header[11] = {40,101,1,0,0,0,0,0,0,0,0};
 if(fwrite(header,1,sizeof(header),stdout)!=sizeof(header)) return 92;
 fflush(stdout);
 if(!strcmp(getenv("FIXTURE_MODE"),"abort")) raise(SIGABRT);
 int c;
 while((c=getchar())!=EOF) { if(putchar(c)==EOF) return 93; fflush(stdout); }
 return 0;
}
'''
TREE_KILL = r'''
using System.Diagnostics;
var info = new ProcessStartInfo(args[0]) { WorkingDirectory=Path.GetDirectoryName(args[0])!, UseShellExecute=false, RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true };
info.ArgumentList.Add("--lumyte-helper");
using var p = Process.Start(info)!;
var stderr = p.StandardError.ReadToEndAsync();
var ready = new byte[44];
if (args.Length > 3) await Task.Delay(250);
else await p.StandardOutput.BaseStream.ReadExactlyAsync(ready).AsTask().WaitAsync(TimeSpan.FromSeconds(12));

p.Kill(entireProcessTree:true);
await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
await File.WriteAllTextAsync(args[1],p.ExitCode.ToString());
await p.StandardOutput.BaseStream.CopyToAsync(Stream.Null).WaitAsync(TimeSpan.FromSeconds(5));
await File.WriteAllTextAsync(args[2],await stderr.WaitAsync(TimeSpan.FromSeconds(5)));
Directory.Delete(Environment.GetEnvironmentVariable("HOME")!, recursive:true);
'''

def run(cmd, **kwargs):
    return sp.run(cmd, check=True, timeout=40, **kwargs)

def processes(marker):
    found = []
    for p in Path('/proc').glob('[0-9]*'):
        try:
            if marker.encode() in (p/'environ').read_bytes():
                found.append(int(p.name))
        except (OSError, PermissionError): pass
    return found

def reap():
    while True:
        try:
            pid, _ = os.waitpid(-1, os.WNOHANG)
            if not pid: break
        except ChildProcessError: break

def snapshot():
    # Read-only; no bus method calls or portal activation.
    cmd = ['systemctl','--user','show-environment']
    env = sp.run(cmd, capture_output=True, timeout=5).stdout.decode()
    env = '\n'.join(sorted(x for x in env.splitlines() if x.split('=')[0] in
        ('DISPLAY','WAYLAND_DISPLAY','XDG_CURRENT_DESKTOP','XDG_SESSION_TYPE','DBUS_SESSION_BUS_ADDRESS')))
    services = sp.run(['systemctl','--user','show','xdg-desktop-portal.service','xdg-desktop-portal-gtk.service',
        '-p','MainPID','-p','ActiveState','-p','SubState','-p','ExecMainStatus'],capture_output=True,timeout=5).stdout.decode()
    settings = {str(p): p.read_text() for p in (Path('/proc/sys/kernel/unprivileged_userns_clone'),Path('/proc/sys/kernel/apparmor_restrict_unprivileged_userns')) if p.exists()}
    configs = {}
    for base in (Path.home()/'.config/xdg-desktop-portal',Path('/etc/xdg/xdg-desktop-portal')):
        if base.exists():
            for p in base.rglob('*'):
                if p.is_file(): configs[str(p)] = hashlib.sha256(p.read_bytes()).hexdigest()
    return dict(manager=env,services=services,settings=settings,configs=configs)

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--weston-root',type=Path,required=True)
    ap.add_argument('--out',type=Path,required=True)
    ap.add_argument('--native-helper',type=Path,help='optional real ELF: assert sandbox-enabled native failure, never rendering success')
    a=ap.parse_args(); out=a.out.resolve(); out.mkdir(parents=True,exist_ok=False)
    resource.setrlimit(resource.RLIMIT_CORE,(0,0))
    # Adopt children of intentionally killed .NET tree owners; reap test zombies.
    assert ctypes.CDLL(None).prctl(36,1,0,0,0)==0
    before=snapshot(); (out/'host-before.json').write_text(json.dumps(before,indent=2))
    stage=out/'stage with spaces'; stage.mkdir()
    wrapper=stage/'lumyte-cef-helper-isolated'
    shutil.copy2(REPO/'tools/setup/run-cef-helper-isolated.sh',wrapper); wrapper.chmod(0o755)
    (out/'fixture.c').write_text(FIXTURE)
    run(['gcc','-Wall','-Wextra','-Werror','-o',str(stage/'lumyte-cef-helper'),str(out/'fixture.c')],stdout=sp.DEVNULL)
    if a.native_helper:
        native_stage=out/'native stage';native_stage.mkdir()
        for source in a.native_helper.resolve().parent.iterdir():
            if source.name!='lumyte-cef-helper-isolated':(native_stage/source.name).symlink_to(source)
        shutil.copy2(wrapper,native_stage/wrapper.name)
    project=out/'tree-kill';project.mkdir()
    (project/'tree-kill.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>')
    (project/'Program.cs').write_text(TREE_KILL)
    (project/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    sdk=sp.check_output(['dotnet','--version'],cwd='/tmp',text=True).strip()
    (project/'global.json').write_text(json.dumps(dict(sdk=dict(version=sdk))))
    for name in ('Directory.Build.props','Directory.Build.targets'):
        (project/name).write_text('<Project />')
    with (out/'dotnet-build.log').open('w') as f: run(['dotnet','build',str(project),'-o',str(project/'bin'),'-v:q'],cwd=project,stdout=f,stderr=sp.STDOUT)
    cases=[]; compositor=None
    runtime=Path(tempfile.mkdtemp(prefix='lumyte-launcher-display-'));runtime.chmod(0o700)
    try:
        # Missing display/dependency are tested before creating any private bus.
        base=dict(os.environ)
        for k in ('DISPLAY','WAYLAND_DISPLAY','XAUTHORITY'):base.pop(k,None)
        for mode in ('no-display','missing-dependency'):
            env=dict(base)
            if mode=='missing-dependency':
                path=out/'minimal-path';path.mkdir();(path/'readlink').symlink_to('/usr/bin/readlink')
                env.update(DISPLAY=':unused',PATH=str(path))
            t=time.monotonic();r=sp.run([str(wrapper),'--lumyte-helper'],input=b'',capture_output=True,env=env,timeout=3)
            assert r.returncode and not r.stdout and time.monotonic()-t<3
            assert (b'display unavailable' if mode=='no-display' else b'missing dependency: dbus-run-session') in r.stderr
            cases.append(dict(case=mode,exit=r.returncode,residual_count=0));(out/(mode+'.stderr')).write_bytes(r.stderr)
        for invalid in (['--no-sandbox'], ['--lumyte-helper','--no-sandbox'], ['--lumyte-helper','--lumyte-allow-unsandboxed=false'], ['--lumyte-helper','--lumyte-allow-unsandboxed','--remote-debugging-port=9222'], ['--type=renderer']):
            r=sp.run([str(wrapper),*invalid],input=b'',capture_output=True,env=base,timeout=3)
            assert r.returncode==78 and not r.stdout and b'expected --lumyte-helper' in r.stderr
        cases.append(dict(case='argument-whitelist-5',exit=78,residual_count=0))
        weston=a.weston_root.resolve();lib=weston/'usr/lib/x86_64-linux-gnu'
        env=dict(base,XDG_RUNTIME_DIR=str(runtime),WAYLAND_DISPLAY='w',
            LD_LIBRARY_PATH=f'{lib}:{lib}/weston:{lib}/libweston-14',
            WESTON_MODULEDIR=str(lib/'libweston-14'),WESTON_DATA_DIR=str(weston/'usr/share/weston'),
            WESTON_MODULE_MAP=';'.join(f'{p.name}={p}' for p in (weston/'usr/lib').rglob('*.so'))+';')
        log=(out/'weston.log').open('wb')
        compositor=sp.Popen([str(weston/'usr/bin/weston'),'--backend=headless','--renderer=pixman',
            '--shell='+str(lib/'weston/kiosk-shell.so'),'--no-config','--socket=w','--width=128','--height=128'],env=env,stdout=log,stderr=log,start_new_session=True)
        deadline=time.monotonic()+6
        while not (runtime/'w').is_socket():
            assert compositor.poll() is None and time.monotonic()<deadline
            time.sleep(.05)
        # Only the dedicated compositor socket reaches launcher; no host display.
        base.update(WAYLAND_DISPLAY=str(runtime/'w'),XDG_RUNTIME_DIR=str(runtime))
        for mode in ('explicit-unsandboxed','normal-eof','abort','term','tree-kill','startup-fail','portal-timeout','tree-kill-startup') + (('native-sandbox-failure',) if a.native_helper else ()):
            root=Path(tempfile.mkdtemp(prefix='lumyte-browser-'));root.chmod(0o700)
            marker='LUMYTE_LAUNCHER_SMOKE='+root.name
            env=dict(base,HOME=str(root),TMPDIR=str(root),XDG_CONFIG_HOME=str(root),XDG_DATA_HOME=str(root),XDG_CACHE_HOME=str(root),
                FIXTURE_PROOF=str(out/(mode+'.proof')),FIXTURE_MODE='abort' if mode=='abort' else 'normal',LUMYTE_LAUNCHER_SMOKE=root.name)
            if mode=='startup-fail':env['WAYLAND_DISPLAY']=str(runtime/'missing')
            target=native_stage/wrapper.name if mode=='native-sandbox-failure' else wrapper
            if mode in ('portal-timeout','tree-kill-startup'):
                tools=out/(mode+'-path');tools.mkdir()
                (tools/'gdbus').write_text('#!/bin/sh\nexit 1\n');(tools/'gdbus').chmod(0o755)
                env['PATH']=str(tools)+':'+base['PATH']
            try:
                if mode in ('tree-kill','tree-kill-startup'):
                    r=sp.run(['dotnet',str(project/'bin/tree-kill.dll'),str(wrapper),str(out/(mode+'-exit')),str(out/(mode+'.stderr'))]+(['early'] if mode=='tree-kill-startup' else []),env=env,capture_output=True,timeout=20)
                    assert r.returncode==0,(r.stdout,r.stderr)
                    code=int((out/(mode+'-exit')).read_text());assert code==137
                    stdout=b''; stderr=b''
                else:
                    p=sp.Popen([str(target),'--lumyte-helper']+(['--lumyte-allow-unsandboxed'] if mode=='explicit-unsandboxed' else []),cwd=target.parent,env=env,stdin=sp.PIPE,stdout=sp.PIPE,stderr=sp.PIPE)
                    if mode in ('normal-eof','explicit-unsandboxed','abort','term'):
                        assert select.select([p.stdout],[],[],12)[0],mode
                        ready=p.stdout.read(44)
                        assert ready==struct.pack('<11I',40,101,1,0,0,0,0,0,0,0,0),(mode,ready,p.poll())
                        if mode=='term':p.terminate()
                    if mode=='native-sandbox-failure':
                        body=b'http://127.0.0.1:1/'
                        packet=struct.pack('<I',40+len(body))+struct.pack('<IQqqiiI',1,1,0,0,800,600,len(body))+body
                        p.stdin.write(packet);p.stdin.flush();p.wait(timeout=12)
                    payload=b'\x00\xffbinary\n;$(touch INJECTION)'
                    stdout,stderr=p.communicate(payload if mode in ('normal-eof','explicit-unsandboxed') else b'',timeout=12)
                    code=p.returncode
                    (out/(mode+'.stderr')).write_bytes(stderr)
                    if mode in ('normal-eof','explicit-unsandboxed'):assert code==0 and stdout==payload,(code,stdout,stderr)
                    if mode=='abort':assert code==134 and not stdout,(mode,code,stdout,stderr)
                    if mode=='native-sandbox-failure':assert code==133 and not stdout and b'SUID sandbox helper binary' in stderr,(mode,code,stdout,stderr)
                    if mode in ('term','startup-fail','portal-timeout','tree-kill-startup'):assert code!=0 and not stdout
                    if mode=='portal-timeout':assert b'private portals not ready' in stderr
                    (out/(mode+'.stderr')).write_bytes(stderr)
                deadline=time.monotonic()+4
                while processes(marker) and time.monotonic()<deadline:reap();time.sleep(.05)
                residual=processes(marker);assert not residual,(mode,residual)
                assert not (stage/'INJECTION').exists()
                mounts=[line for line in Path('/proc/self/mountinfo').read_text().splitlines() if str(root) in line]
                assert not mounts,(mode,mounts)
                proof=out/(mode+'.proof')
                if proof.exists():
                    _,address,xdg=proof.read_text().splitlines()
                    assert xdg==str(root/'portal-runtime')
                    assert address!=os.environ.get('DBUS_SESSION_BUS_ADDRESS')
                    child_exe,child_bus=Path(str(proof)+'.child').read_text().splitlines()
                    assert child_exe==str(stage/'lumyte-cef-helper') and child_bus==address
                    # Raw address is test-only evidence, never a launcher diagnostic.
                    query=sp.run(['gdbus','call','--address',address,'--dest','org.freedesktop.DBus','--object-path','/org/freedesktop/DBus','--method','org.freedesktop.DBus.ListNames'],capture_output=True,timeout=2)
                    assert query.returncode!=0
                    socket_path=address.removeprefix('unix:path=').split(',')[0]
                    assert socket_path==str(root/'portal-runtime/bus')
                    assert not Path(socket_path).exists(),socket_path
                cases.append(dict(case=mode,exit=code,residual_count=len(residual),mount_count=len(mounts)))
                for logname in ('portal.log','portal-gtk.log'):
                    if (root/logname).exists():shutil.copy2(root/logname,out/(mode+'-'+logname))
            finally:
                for name in ('portal.log','portal-gtk.log','private-bus.log'):
                    if (root/name).exists():shutil.copy2(root/name,out/(mode+'-'+name))
                # Safety fallback only exact owned environment marker, never name-kill.
                for pid in processes(marker):
                    try:os.kill(pid,signal.SIGKILL)
                    except ProcessLookupError:pass
                reap()
                if root.exists():shutil.rmtree(root)
    finally:
        if compositor:
            try:os.killpg(compositor.pid,signal.SIGTERM)
            except ProcessLookupError:pass
            compositor.wait(timeout=5)
        reap()
        assert not processes(str(runtime)),'owned compositor residues'
        shutil.rmtree(runtime)
        after=snapshot();(out/'host-after.json').write_text(json.dumps(after,indent=2))
        assert before==after,'host settings changed'
        (out/'results.json').write_text(json.dumps(dict(cases=cases,host_unchanged=before==after),indent=2))
    print(json.dumps(dict(cases=cases,host_unchanged=True)))
if __name__=='__main__':main()
