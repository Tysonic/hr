import { describe, expect, it } from 'vitest'
import { money, slugPattern, toSlug, words } from './format'
import { can } from './policies'

describe('format', () => {
  it('formats money to the currency minor units', () => {
    expect(money(1250000, { currencyCode: 'UGX', currencyMinorUnits: 0 })).toBe('UGX 1,250,000')
    expect(money(1250.5, { currencyCode: 'KES', currencyMinorUnits: 2 })).toBe('KES 1,250.50')
    expect(money(null, { currencyCode: 'UGX', currencyMinorUnits: 0 })).toBe('—')
  })

  it('turns a typed name into a company code', () => {
    expect(toSlug('  Acme Uganda Ltd. ')).toBe('acme-uganda-ltd')
    expect(slugPattern.test(toSlug('Acme Uganda'))).toBe(true)
    expect(slugPattern.test('-acme')).toBe(false)
  })

  it('splits PascalCase', () => {
    expect(words('CompanyProfile')).toBe('Company Profile')
  })
})

describe('permissions', () => {
  it('limits tenant management to platform admins', () => {
    expect(can(['platform-admin'], 'platformTenants')).toBe(true)
    expect(can(['hr-admin'], 'platformTenants')).toBe(false)
    expect(can(['hr-admin'], 'companySettings')).toBe(true)
    expect(can(['employee'], 'companySettings')).toBe(false)
  })
})
