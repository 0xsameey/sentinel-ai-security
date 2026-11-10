import {useEffect,useState,useRef} from "react";
import {delegatedApi,type DelegationView,type DelegatedPayment} from "../api";
export type ObservedPayment=DelegatedPayment&{createdAtUtc?:string};
export function usePaymentActivity(token:string|null,revision:number){
 const [views,setViews]=useState<DelegationView[]>([]);const [loading,setLoading]=useState(false);const [error,setError]=useState("");
 const previousToken=useRef<string|null>(null);
 useEffect(()=>{
   let active=true;if(previousToken.current!==token)setViews([]);previousToken.current=token;setError("");if(!token){setViews([]);setLoading(false);return;}
   setLoading(true);
   void (async()=>{try{
     const list=await delegatedApi.list(token);
     const results=await Promise.allSettled(list.slice(0,20).map(item=>delegatedApi.inspect(token,item.id)));
     if(!active)return;
     setViews(results.flatMap(result=>result.status==="fulfilled"?[result.value]:[]));
     if(results.some(result=>result.status==="rejected"))setError("Some delegation activity could not be loaded. The records below may be incomplete.");
   }catch{if(active)setError("Payment activity is unavailable. Connect and sign in as the owner, then refresh.");}
   finally{if(active)setLoading(false);}})();
   return()=>{active=false;};
 },[token,revision]);
 const payments=views.flatMap(view=>view.payments as ObservedPayment[]).sort((a,b)=>(b.createdAtUtc??"").localeCompare(a.createdAtUtc??""));
 return {views,payments,loading,error};
}
