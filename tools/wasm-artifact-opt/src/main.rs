// Local artifact processing only. No RPC, wallet, CLI-config or deployment code.
fn main() -> Result<(), Box<dyn std::error::Error>> {
    let args: Vec<_> = std::env::args_os().collect();
    if args.len() != 3 { return Err("Usage: sentinel-wasm-artifact-opt INPUT.wasm OUTPUT.wasm".into()); }
    let input = std::path::Path::new(&args[1]);
    let output = std::path::Path::new(&args[2]);
    if output.exists() { return Err("Refusing to overwrite an existing artifact".into()); }
    let mut options = wasm_opt::OptimizationOptions::new_optimize_for_size();
    options.enable_feature(wasm_opt::Feature::TruncSat);
    options.enable_feature(wasm_opt::Feature::BulkMemory);
    options.run(input, output)?;
    Ok(())
}
