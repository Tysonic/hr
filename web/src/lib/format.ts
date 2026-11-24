import dayjs from 'dayjs'
import type { Country } from '../api/types'

/** An amount in the company's currency, to its minor units: "UGX 1,250,000", "KES 1,250.50". */
export function money(amount: number | null | undefined, country: Pick<Country, 'currencyCode' | 'currencyMinorUnits'>) {
  if (amount === null || amount === undefined) return '—'
  const formatted = amount.toLocaleString('en', {
    minimumFractionDigits: country.currencyMinorUnits,
    maximumFractionDigits: country.currencyMinorUnits,
  })
  return `${country.currencyCode} ${formatted}`
}

export function date(value: string | null | undefined) {
  return value ? dayjs(value).format('D MMM YYYY') : '—'
}

export function dateTime(value: string | null | undefined) {
  return value ? dayjs(value).format('D MMM YYYY, HH:mm') : '—'
}

/** Splits PascalCase names for display: "CompanyProfile" → "Company Profile". */
export function words(value: string | null | undefined) {
  return value ? value.replace(/([a-z])([A-Z])/g, '$1 $2') : '—'
}

export const months = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
].map((label, i) => ({ value: String(i + 1), label }))

/** A company code as typed: lowercased, spaces to hyphens, anything else dropped. */
export function toSlug(value: string) {
  return value.toLowerCase().trim().replace(/\s+/g, '-').replace(/[^a-z0-9-]/g, '').replace(/-+/g, '-').slice(0, 63)
}

export const slugPattern = /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])$/
