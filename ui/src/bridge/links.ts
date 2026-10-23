import type { LinksInfo, SetLinksArgs } from './types'

// A successful partial write must also become the local comparison baseline.
// Ignore undefined fields, but retain explicit false and numeric zero.
export function mergeLinks(info: LinksInfo, args: SetLinksArgs): LinksInfo {
  const next = { ...info }
  if (args.codex !== undefined) next.codex = args.codex
  if (args.claude !== undefined) next.claude = args.claude
  if (args.dsh !== undefined) next.dsh = args.dsh
  if (args.signal !== undefined) next.signal = args.signal
  if (args.dotMatrix !== undefined) next.dotMatrix = args.dotMatrix
  if (args.idleOffsetMs !== undefined) next.idleOffsetMs = args.idleOffsetMs
  if (args.idleRatePercent !== undefined) next.idleRatePercent = args.idleRatePercent
  return next
}
