// 语言表单一来源门：resources/languages.json 里的每个 id 必须真能被内核高亮，
// 且扩展名/展示名不重复。跑法：node tools/check-languages.mjs（build.ps1 会调）
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import { hasLanguage } from '../kernel/src/kernel/components/code/languages.js'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const listPath = join(root, 'resources', 'languages.json')
const languages = JSON.parse(readFileSync(listPath, 'utf8'))

const problems = []
const seenIds = new Set()
const seenExtensions = new Map()
const seenNames = new Set()

for (const entry of languages) {
    const { id, displayName, extensions } = entry
    if (!id || typeof id !== 'string') problems.push(`语言缺少 id: ${JSON.stringify(entry)}`)
    if (seenIds.has(id)) problems.push(`id 重复: ${id}`)
    seenIds.add(id)

    if (!displayName) problems.push(`缺展示名: ${id}`)
    if (seenNames.has(displayName)) problems.push(`展示名重复: ${displayName}`)
    seenNames.add(displayName)

    if (!Array.isArray(extensions) || extensions.length === 0) problems.push(`缺扩展名: ${id}`)
    for (const ext of extensions ?? []) {
        if (ext !== ext.toLowerCase()) problems.push(`扩展名需小写: ${id} -> ${ext}`)
        if (seenExtensions.has(ext)) problems.push(`扩展名重复: ${ext}（${seenExtensions.get(ext)} 与 ${id}）`)
        seenExtensions.set(ext, id)
    }

    if (!hasLanguage(id)) problems.push(`内核无法高亮该 id: ${id}（需要在 languages.js 注册语法或从清单移除）`)
}

if (problems.length > 0) {
    for (const problem of problems) console.error('  ✗ ' + problem)
    console.error(`check-languages: ${problems.length} 个问题（${languages.length} 门语言）`)
    process.exit(1)
}

console.log(`check-languages: OK（${languages.length} 门语言，${seenExtensions.size} 个扩展名）`)
