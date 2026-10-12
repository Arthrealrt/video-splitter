import subprocess, json, pathlib, math, sys, uuid
root=pathlib.Path(__file__).resolve().parent.parent
app=root if (root/'VideoSplitter.cs').exists() else root/'outputs'/'视频分段工具'
(root/'work').mkdir(exist_ok=True)
work=root/'work'/('verification-v11-'+uuid.uuid4().hex[:8])
work.mkdir(exist_ok=True)
ff=app/'ffmpeg.exe'
probe=app/'ffprobe.exe'
exe=app/'视频分段工具.exe'
def run(args):
    result=subprocess.run([str(x) for x in args],capture_output=True,timeout=120)
    if result.returncode: raise RuntimeError(result.stderr.decode('utf-8',errors='replace') or str(args))
    return result.stdout
def info(path):
    return json.loads(run([probe,'-v','error','-show_streams','-show_format','-of','json',path]))
def make(name,seconds,audio=True,codec='libx264'):
    path=work/name
    args=[ff,'-v','error','-y','-f','lavfi','-i','testsrc2=size=320x240:rate=25']
    if audio: args+=['-f','lavfi','-i','sine=frequency=440:sample_rate=48000']
    args+=['-t',str(seconds),'-c:v',codec]
    if audio: args+=['-c:a','aac']
    args+=[path]
    run(args)
    return path
results=[]
def split(source,seconds,expected,audio=True,discard=False):
    dest=work/('结果 '+str(len(results)))
    args=[exe,'--split',source,dest,seconds]
    if discard: args+=['--discard-tail']
    run(args)
    files=sorted(dest.rglob('片段_*.mp4')) if dest.exists() else []
    assert len(files)==len(expected),(len(files),expected)
    for clip,length in zip(files,expected):
        data=info(clip)
        video=next(x for x in data['streams'] if x['codec_type']=='video')
        assert abs(float(video['duration'])-length)<=0.041,(clip,video['duration'],length)
        assert video['codec_name']=='h264'
        assert any(x['codec_type']=='audio' for x in data['streams'])==audio
        run([ff,'-v','error','-i',clip,'-f','null','-'])
    results.append({'input':source.name,'segment_seconds':seconds,'discard_tail':discard,'clips':[float(next(x for x in info(c)['streams'] if x['codec_type']=='video')['duration']) for c in files],'passed':True})
    return files
mp4=make('中文 文件 19秒.mp4',19)
clips=split(mp4,6,[6,6,6,1])
split(mp4,8,[8,8,3])
split(mp4,7.5,[7.5,7.5,4])
split(mp4,30,[19])
split(mp4,8,[8,8],discard=True)
split(mp4,15,[15,4])
split(mp4,6,[6,6,6],discard=True)
split(mp4,15,[15],discard=True)
mkv=make('无音频.mkv',7,False,'ffv1')
split(mkv,6,[6,1],False)
mov=work/'手机格式.mov'
run([ff,'-v','error','-y','-i',mp4,'-c','copy',mov])
split(mov,15,[15,4])
avi=make('传统格式.avi',7,False,'mpeg4')
split(avi,6,[6,1],False)
exact=make('整倍数.mp4',12)
split(exact,6,[6,6])
short=make('短视频.mp4',2)
split(short,6,[2])
split(short,6,[],discard=True)
# Check that every segment begins at the requested source time, using the
# first decoded gray frame. Lossy encoding permits small pixel differences.
def frame(path,time=0):
    return run([ff,'-v','error','-ss',str(time),'-i',path,'-frames:v','1','-pix_fmt','gray','-f','rawvideo','-'])
psnrs=[]
for index,clip in enumerate(clips):
    a,b=frame(mp4,index*6),frame(clip)
    assert len(a)==len(b)>0
    mse=sum((x-y)**2 for x,y in zip(a,b))/len(a)
    psnr=10*math.log10(255**2/mse) if mse else 100
    assert psnr>30,(index,psnr)
    psnrs.append(round(psnr,2))
invalid=work/'损坏.mp4'
invalid.write_bytes(b'not a video')
failed=subprocess.run([str(exe),'--split',str(invalid),str(work/'损坏输出'),'6'],timeout=30)
assert failed.returncode==1
err=app/'命令行错误.txt'
assert err.exists()
err.unlink()
for bad_seconds in ['0','-1','0.09','86401','NaN','Infinity','abc']:
    rejected=subprocess.run([str(exe),'--split',str(mp4),str(work/'invalid-duration'),bad_seconds],timeout=30)
    assert rejected.returncode==1,bad_seconds
    assert not (work/'invalid-duration').exists()
err.unlink()
report={'cases':results,'segment_start_psnr_db':psnrs,'invalid_input_rejected':True,'invalid_durations_rejected':True}
(work/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
