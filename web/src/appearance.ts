import { useQuery } from '@tanstack/react-query';
import { get } from './api';
import type { ColorPreference } from './colorMode';

/** Form families (the look of a form) and the two built-in palettes they can use. */
export const formFamilies = ['report', 'worksheet', 'checklist', 'certificate', 'investigation', 'register', 'plan'] as const;
export type FormFamily = (typeof formFamilies)[number];
export type PaletteKey = 'palette1' | 'palette2' | 'custom';
export interface FamilyPalette { palette: PaletteKey; edge?: string | null; accent?: string | null }
export interface AppearanceValues { mode: ColorPreference | null; primaryColor: string | null; accentColor: string | null; formPalettes: Partial<Record<FormFamily, FamilyPalette>> | null }
export interface AppearanceInfo {
  organization: AppearanceValues | null; personal: AppearanceValues | null; canEditOrganization: boolean;
  defaults: { mode: ColorPreference; primaryColor: string; accentColor: string; formPalettes: Record<FormFamily, FamilyPalette>; palettes: Record<'palette1' | 'palette2', { edge: string; accent: string }> };
}

export const paletteNames: Record<PaletteKey, string> = { palette1: 'Green and blue', palette2: 'Slate and blue', custom: 'Custom colours' };
export const familyNames: Record<FormFamily, string> = {
  report: 'Reports', worksheet: 'Engineering worksheets', checklist: 'Checklists and audits', certificate: 'Certificates and warrants',
  investigation: 'Investigations (CAPA, 8D)', register: 'Registers and logs', plan: 'Plans',
};

/** Appearance settings of the organization and the signed-in person (only fetched once signed in). */
export const useAppearance = (enabled: boolean) =>
  useQuery({ queryKey: ['appearance'], queryFn: () => get<AppearanceInfo>('/api/appearance'), enabled, staleTime: 5 * 60_000 });

/** What applies: the person's override, else the organization default, else the built-in look (null keeps the built-in value). */
export function effectiveAppearance(info: AppearanceInfo | undefined) {
  const o = info?.organization, p = info?.personal;
  const forms: Partial<Record<FormFamily, FamilyPalette>> = {};
  for (const f of formFamilies) { const v = p?.formPalettes?.[f] ?? o?.formPalettes?.[f]; if (v) forms[f] = v; }
  return {
    personalMode: p?.mode ?? null,
    organizationMode: o?.mode ?? null,
    primaryColor: p?.primaryColor ?? o?.primaryColor ?? null,
    accentColor: p?.accentColor ?? o?.accentColor ?? null,
    forms,
  };
}
