import {readFile} from 'node:fs/promises';
import {transform} from 'rolldown/experimental';
export const dataModule=source=>'data:text/javascript;base64,'+Buffer.from(source).toString('base64');
const cache=new Map();
export async function loadSource(name,overrides={}){
 const key=name+JSON.stringify(overrides);if(cache.has(key))return cache.get(key);
 const ext=['presentation','notifications'].includes(name)?'.ts':'.tsx';
 const source=await readFile(new URL(`../src/${name}${ext}`,import.meta.url),'utf8');
 let js=(await transform(name+ext,source,{jsx:{runtime:'automatic'}})).code;
 const specifiers=[...js.matchAll(/(?:from |import )"([^"]+)"/g)].map(match=>match[1]);
 for(const specifier of new Set(specifiers)){
   let target=overrides[specifier];
   if(!target){
     if(specifier==='react'||specifier==='react/jsx-runtime'||specifier==='lucide-react')target=import.meta.resolve(specifier);
     else if(specifier.endsWith('.css')){js=js.replace(`import "${specifier}";`,'');continue;}
     else if(specifier==='../presentation'||specifier==='./presentation')target=await loadSource('presentation');
     else if(specifier==='../notifications'||specifier==='./notifications')target=await loadSource('notifications');
     else if(specifier==='./ui'||specifier==='./components/ui')target=await loadSource('components/ui');
     else throw Error(`Provide safe import override for ${name}: ${specifier}`);
   }
   js=js.replaceAll(JSON.stringify(specifier),JSON.stringify(target));
 }
 const url=dataModule(js);cache.set(key,url);return url;
}
export const inertApi=dataModule('export const delegatedApi={}; export const sentinelApi={};');
export const blockedSigning=dataModule('export const MANUAL_TESTING=false; export const CONTRACT_ID="fixture.testnet"; export const signContractCall=()=>{throw Error("Signing attempted in render")};');
