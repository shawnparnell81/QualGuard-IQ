import { useState } from 'react';
import { Link as RouterLink, Navigate, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Box, Button, Link, Stack, TextField } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { z } from 'zod';
import { get, post, put } from '../api';
import { useAuth } from '../auth';
import { modules } from '../modules/registry';
import { ModuleWorkspace } from '../layout/ModuleWorkspace';
import { DataTable } from '../components/DataTable';
import { StatusChip, fmtDate, masterStatus, ncrStatus, partTypes } from '../components/Status';
import { AttachmentsAndHistory } from '../components/RecordTabs';
import { Facts, Prose, SchemaForm, Section, StepDialog, useRunner, useSites } from '../components/Kit';
import { mono } from '../theme';

const mod = () => modules.find((m) => m.key === 'engineering')!;
const num = (x: { getValue: () => unknown }) => <span style={{ fontFamily: mono }}>{String(x.getValue())}</span>;
const partType = (t: number) => partTypes.find(([v]) => v === t)?.[1] ?? 'Other';
const statusOpts = masterStatus.map((s, i) => [i, s] as [number, string]);

/** Module landing: products for master-data users, drawings otherwise. */
export function EngineeringHome() {
  const { can } = useAuth();
  return <Navigate to={can('masterdata.view') ? '/products/items' : '/drawings'} replace />;
}

function useFilter() {
  const [q, setQ] = useState('');
  const [applied, setApplied] = useState('');
  const bar = (placeholder: string) => (
    <Stack direction="row" gap={1} component="form" sx={{ mb: 2 }} onSubmit={(e) => { e.preventDefault(); setApplied(q); }}>
      <TextField size="small" placeholder={placeholder} value={q} onChange={(e) => setQ(e.target.value)} sx={{ width: 360 }} />
      <Button type="submit" variant="outlined">Filter</Button>
    </Stack>
  );
  return { applied, bar, qs: applied ? `&q=${encodeURIComponent(applied)}` : '' };
}

// ---------- Products ----------
interface ProductRow { id: string; number: string; name: string; category: string | null; status: number; family: string | null; siteCode: string; parts: number }

export function ProductList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const f = useFilter();
  const list = useQuery({ queryKey: ['products', f.applied], queryFn: () => get<{ items: ProductRow[] }>(`/api/master/products?pageSize=200${f.qs}`) });
  const cols: ColumnDef<ProductRow, any>[] = [
    { header: 'Product', accessorKey: 'number', cell: num }, { header: 'Name', accessorKey: 'name' }, { header: 'Family', accessorKey: 'family', cell: (x) => x.getValue() ?? '—' },
    { header: 'Category', accessorKey: 'category', cell: (x) => x.getValue() ?? '—' }, { header: 'Parts', accessorKey: 'parts' },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={masterStatus[x.getValue()]} /> }, { header: 'Site', accessorKey: 'siteCode' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Products' }]} title="Products"
      actions={can('masterdata.edit') ? <Button variant="contained" component={RouterLink} to="/products/items/new">New product</Button> : undefined}>
      {f.bar('Filter by number, name or category')}
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/products/items/${r.id}`)} empty="No products yet." />
    </ModuleWorkspace>
  );
}

const productSchema = z.object({
  number: z.string().trim().min(1, 'Required').max(60), name: z.string().trim().min(1, 'Required').max(200), description: z.string().max(4000).optional(),
  familyId: z.string().optional(), category: z.string().max(100).optional(), siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(), ownerId: z.string().optional(), status: z.coerce.number().int(),
});

function useFamilies() { return useQuery({ queryKey: ['families'], queryFn: () => get<{ id: string; code: string; name: string }[]>('/api/master/families') }); }

export function ProductNew() {
  const nav = useNavigate();
  const fam = useFamilies();
  const qc = useQueryClient();
  const [dlg, setDlg] = useState(false);
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Products', to: '/products/items' }, { label: 'New' }]} title="New product"
      actions={<Button variant="outlined" onClick={() => setDlg(true)}>Add product family</Button>}>
      <SchemaForm schema={productSchema} submitLabel="Create product" onCancel={() => nav('/products/items')}
        defaults={{ number: '', name: '', description: '', familyId: '', category: '', siteId: '', departmentId: '', ownerId: '', status: 0 }}
        fields={[
          { name: 'number', label: 'Product number' }, { name: 'name', label: 'Name' },
          { name: 'familyId', label: 'Product family', type: 'select', options: [['', 'None'], ...(fam.data ?? []).map((x) => [x.id, `${x.code} — ${x.name}`] as [string, string])] },
          { name: 'category', label: 'Category' }, { name: 'siteId', label: 'Owning site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' },
          { name: 'ownerId', label: 'Product owner', type: 'user' }, { name: 'status', label: 'Status', type: 'select', options: statusOpts },
          { name: 'description', label: 'Description', type: 'multiline' },
        ]}
        onSubmit={async (v) => { const r = await post<{ id: string }>('/api/master/products', { ...v, familyId: v.familyId || null, departmentId: v.departmentId || null, ownerId: v.ownerId || null }); nav(`/products/items/${r.id}`); }} />
      <StepDialog open={dlg} title="Add product family" confirm="Add" onClose={() => setDlg(false)} inputs={[{ name: 'code', label: 'Code' }, { name: 'name', label: 'Name' }, { name: 'regulatoryCategory', label: 'Regulatory category (optional)', helper: 'Used by retention policies, e.g. FDA Class II or IATF safety' }]}
        onConfirm={async (v) => { await post('/api/master/families', { ...v, regulatoryCategory: v.regulatoryCategory || null }); qc.invalidateQueries({ queryKey: ['families'] }); }} />
    </ModuleWorkspace>
  );
}

interface ProductDto { id: string; number: string; name: string; description: string | null; familyId: string | null; category: string | null; siteId: string; departmentId: string | null; ownerId: string | null; status: number; rowVersion: number; ownerName: string | null;
  parts: { id: string; partNumber: string; name: string; revision: string; partType: number; status: number }[]; ncrs: { id: string; number: string; title: string; status: number }[] | null; canEdit: boolean }

export function ProductDetail() {
  const { id } = useParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const [tab, setTab] = useState('overview');
  const [dlg, setDlg] = useState(false);
  const sites = useSites();
  const fam = useFamilies();
  const q = useQuery({ queryKey: ['product', id], queryFn: () => get<ProductDto>(`/api/master/products/${id}`) });
  const { error, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['product', id] }));
  const d = q.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Products', to: '/products/items' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Products', to: '/products/items' }, { label: d.number }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.number}</span><span>{d.name}</span><StatusChip status={masterStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Overview', value: 'overview' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={d.canEdit ? <Stack direction="row" gap={1}><Button variant="outlined" onClick={() => setDlg(true)}>Edit</Button><Button variant="contained" component={RouterLink} to={`/products/parts/new?product=${d.id}`}>Add part</Button></Stack> : undefined}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: 3 }}>
        <Facts items={[['Family', fam.data?.find((f) => f.id === d.familyId)?.name ?? '—'], ['Category', d.category ?? '—'], ['Owner', d.ownerName ?? '—'], ['Site', sites.data?.find((s) => s.id === d.siteId)?.code ?? '—']]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'overview' && <Stack gap={2}>
            {d.description && <Section title="Description"><Prose label="Description" text={d.description} /></Section>}
            <Section title="Parts">
              <DataTable data={d.parts} columns={[{ header: 'Part', accessorKey: 'partNumber', cell: num }, { header: 'Name', accessorKey: 'name' }, { header: 'Rev', accessorKey: 'revision' },
                { header: 'Type', accessorKey: 'partType', cell: (x) => partType(x.getValue() as number) }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={masterStatus[x.getValue() as number]} /> }]}
                onRowClick={(r) => nav(`/products/parts/${r.id}`)} empty="No parts linked to this product." />
            </Section>
            {d.ncrs && <Section title="Related nonconformances">
              {d.ncrs.length === 0 ? <Box sx={{ color: 'text.secondary', fontSize: 14 }}>None.</Box> :
                d.ncrs.map((n) => <Box key={n.id} sx={{ py: 0.5 }}><Link component={RouterLink} to={`/ncr/${n.id}`} sx={{ fontFamily: mono }}>{n.number}</Link> {n.title} <StatusChip status={ncrStatus[n.status]} /></Box>)}
            </Section>}
          </Stack>}
          {tab === 'more' && <AttachmentsAndHistory type="Product" id={d.id} canEdit={d.canEdit} />}
        </Box>
      </Box>
      <StepDialog open={dlg} title="Edit product" confirm="Save" onClose={() => setDlg(false)}
        inputs={[{ name: 'name', label: 'Name', initial: d.name }, { name: 'category', label: 'Category', initial: d.category ?? '' },
          { name: 'familyId', label: 'Family', type: 'select', initial: d.familyId ?? '', options: [['', 'None'], ...(fam.data ?? []).map((x) => [x.id, x.name] as [string, string])] },
          { name: 'status', label: 'Status', type: 'select', initial: String(d.status), options: statusOpts }, { name: 'description', label: 'Description', type: 'multiline', initial: d.description ?? '' }]}
        onConfirm={(v) => run(() => put(`/api/master/products/${d.id}`, { number: d.number, name: v.name, description: v.description || null, familyId: v.familyId || null, category: v.category || null,
          siteId: d.siteId, departmentId: d.departmentId, ownerId: d.ownerId, status: Number(v.status), rowVersion: d.rowVersion }))} />
    </ModuleWorkspace>
  );
}

// ---------- Parts ----------
interface PartRow { id: string; partNumber: string; name: string; revision: string; partType: number; unitOfMeasure: string; status: number; hasSpecialCharacteristics: boolean; product: string | null; siteCode: string }

export function PartList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const f = useFilter();
  const list = useQuery({ queryKey: ['parts', f.applied], queryFn: () => get<{ items: PartRow[] }>(`/api/master/parts?pageSize=200${f.qs}`) });
  const cols: ColumnDef<PartRow, any>[] = [
    { header: 'Part number', accessorKey: 'partNumber', cell: num }, { header: 'Name', accessorKey: 'name' }, { header: 'Rev', accessorKey: 'revision' },
    { header: 'Type', accessorKey: 'partType', cell: (x) => partType(x.getValue()) }, { header: 'Product', accessorKey: 'product', cell: (x) => x.getValue() ?? '—' },
    { header: 'Special char.', accessorKey: 'hasSpecialCharacteristics', cell: (x) => (x.getValue() ? 'Yes' : '—') },
    { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={masterStatus[x.getValue()]} /> }, { header: 'Site', accessorKey: 'siteCode' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Parts' }]} title="Parts"
      actions={can('masterdata.edit') ? <Button variant="contained" component={RouterLink} to="/products/parts/new">New part</Button> : undefined}>
      {f.bar('Filter by part number, name or material')}
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/products/parts/${r.id}`)} empty="No parts yet." />
    </ModuleWorkspace>
  );
}

const partSchema = z.object({
  partNumber: z.string().trim().min(1, 'Required').max(60), name: z.string().trim().min(1, 'Required').max(200), revision: z.string().trim().min(1, 'Required').max(10),
  partType: z.coerce.number().int(), unitOfMeasure: z.string().trim().min(1, 'Required').max(20), productId: z.string().optional(), material: z.string().max(200).optional(),
  hasSpecialCharacteristics: z.coerce.number().int(), siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(), status: z.coerce.number().int(), description: z.string().max(4000).optional(),
});

export function PartNew() {
  const nav = useNavigate();
  const [sp] = useSearchParams();
  const product = sp.get('product') ?? '';
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Parts', to: '/products/parts' }, { label: 'New' }]} title="New part">
      <SchemaForm schema={partSchema} submitLabel="Create part" onCancel={() => nav('/products/parts')}
        defaults={{ partNumber: '', name: '', revision: 'A', partType: 0, unitOfMeasure: 'EA', productId: product, material: '', hasSpecialCharacteristics: 0, siteId: '', departmentId: '', status: 0, description: '' }}
        fields={[
          { name: 'partNumber', label: 'Part number' }, { name: 'name', label: 'Name' }, { name: 'revision', label: 'Initial revision' },
          { name: 'partType', label: 'Type', type: 'select', options: partTypes }, { name: 'unitOfMeasure', label: 'Unit of measure' },
          { name: 'productId', label: 'Product', type: 'lookup', lookup: 'product' }, { name: 'material', label: 'Material' },
          { name: 'hasSpecialCharacteristics', label: 'Special characteristics', type: 'select', options: [[0, 'No'], [1, 'Yes']] },
          { name: 'siteId', label: 'Owning site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' },
          { name: 'status', label: 'Status', type: 'select', options: statusOpts }, { name: 'description', label: 'Description', type: 'multiline' },
        ]}
        onSubmit={async (v) => {
          const r = await post<{ id: string }>('/api/master/parts', { ...v, productId: v.productId || null, departmentId: v.departmentId || null, hasSpecialCharacteristics: v.hasSpecialCharacteristics === 1, material: v.material || null, description: v.description || null });
          nav(`/products/parts/${r.id}`);
        }} />
    </ModuleWorkspace>
  );
}

interface PartDto { id: string; partNumber: string; name: string; description: string | null; partType: number; revision: string; unitOfMeasure: string; productId: string | null; material: string | null; hasSpecialCharacteristics: boolean;
  siteId: string; departmentId: string | null; status: number; rowVersion: number; product: { id: string; number: string; name: string } | null; revisions: { id: string; revision: string; reason: string; effectiveOn: string; createdAt: string }[]; canEdit: boolean }

export function PartDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [tab, setTab] = useState('overview');
  const [dlg, setDlg] = useState<string | null>(null);
  const q = useQuery({ queryKey: ['part', id], queryFn: () => get<PartDto>(`/api/master/parts/${id}`) });
  const { error, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['part', id] }));
  const d = q.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Parts', to: '/products/parts' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Parts', to: '/products/parts' }, { label: d.partNumber }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.partNumber}</span><span>{d.name}</span><StatusChip status={`Rev ${d.revision}`} /><StatusChip status={masterStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Overview', value: 'overview' }, { label: 'Revisions', value: 'revs' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={d.canEdit ? <Stack direction="row" gap={1}><Button variant="outlined" onClick={() => setDlg('edit')}>Edit</Button><Button variant="contained" onClick={() => setDlg('revise')}>New revision</Button></Stack> : undefined}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: 3 }}>
        <Facts items={[['Type', partType(d.partType)], ['Revision', d.revision], ['Unit', d.unitOfMeasure], ['Material', d.material ?? '—'], ['Special characteristics', d.hasSpecialCharacteristics ? 'Yes' : 'No'],
          ['Product', d.product ? <Link component={RouterLink} to={`/products/items/${d.product.id}`}>{d.product.number} {d.product.name}</Link> : '—']]} />
        <Box sx={{ minWidth: 0 }}>
          {tab === 'overview' && <Section title="Description">{d.description ? <Prose label="Description" text={d.description} /> : <Box sx={{ color: 'text.secondary', fontSize: 14 }}>No description.</Box>}</Section>}
          {tab === 'revs' && <Section title="Revision history">
            <DataTable data={d.revisions} columns={[{ header: 'Rev', accessorKey: 'revision' }, { header: 'Reason', accessorKey: 'reason' }, { header: 'Effective', accessorKey: 'effectiveOn', cell: (x) => fmtDate(x.getValue() as string) }]} empty="No revisions." />
          </Section>}
          {tab === 'more' && <AttachmentsAndHistory type="Part" id={d.id} canEdit={d.canEdit} />}
        </Box>
      </Box>
      <StepDialog open={dlg === 'revise'} title="New part revision" intro="The current revision stays in the history." confirm="Create revision" onClose={() => setDlg(null)}
        inputs={[{ name: 'revision', label: 'New revision' }, { name: 'reason', label: 'Reason / change reference', type: 'multiline' }, { name: 'effectiveOn', label: 'Effective on (blank = today)', type: 'date' }]}
        onConfirm={(v) => run(() => post(`/api/master/parts/${d.id}/revise`, { revision: v.revision, reason: v.reason, effectiveOn: v.effectiveOn || null }))} />
      <StepDialog open={dlg === 'edit'} title="Edit part" confirm="Save" onClose={() => setDlg(null)}
        inputs={[{ name: 'name', label: 'Name', initial: d.name }, { name: 'material', label: 'Material', initial: d.material ?? '' }, { name: 'unitOfMeasure', label: 'Unit of measure', initial: d.unitOfMeasure },
          { name: 'partType', label: 'Type', type: 'select', initial: String(d.partType), options: partTypes }, { name: 'special', label: 'Special characteristics', type: 'checkbox', initial: String(d.hasSpecialCharacteristics) },
          { name: 'status', label: 'Status', type: 'select', initial: String(d.status), options: statusOpts }, { name: 'description', label: 'Description', type: 'multiline', initial: d.description ?? '' }]}
        onConfirm={(v) => run(() => put(`/api/master/parts/${d.id}`, { partNumber: d.partNumber, name: v.name, description: v.description || null, partType: Number(v.partType), revision: d.revision, unitOfMeasure: v.unitOfMeasure,
          productId: d.productId, material: v.material || null, hasSpecialCharacteristics: v.special === 'true', siteId: d.siteId, departmentId: d.departmentId, status: Number(v.status), rowVersion: d.rowVersion }))} />
    </ModuleWorkspace>
  );
}

// ---------- Customers ----------
interface CustomerRow { id: string; code: string; name: string; contactName: string | null; email: string | null; phone: string | null; status: number; siteCode: string }

export function CustomerList() {
  const nav = useNavigate();
  const { can } = useAuth();
  const f = useFilter();
  const list = useQuery({ queryKey: ['customers', f.applied], queryFn: () => get<{ items: CustomerRow[] }>(`/api/master/customers?pageSize=200${f.qs}`) });
  const cols: ColumnDef<CustomerRow, any>[] = [
    { header: 'Code', accessorKey: 'code', cell: num }, { header: 'Name', accessorKey: 'name' }, { header: 'Contact', accessorKey: 'contactName', cell: (x) => x.getValue() ?? '—' },
    { header: 'Email', accessorKey: 'email', cell: (x) => x.getValue() ?? '—' }, { header: 'Status', accessorKey: 'status', cell: (x) => <StatusChip status={masterStatus[x.getValue()]} /> }, { header: 'Site', accessorKey: 'siteCode' },
  ];
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Customers' }]} title="Customers"
      actions={can('masterdata.edit') ? <Button variant="contained" component={RouterLink} to="/products/customers/new">New customer</Button> : undefined}>
      {f.bar('Filter by code, name or contact')}
      <DataTable data={list.data?.items ?? []} columns={cols} onRowClick={(r) => nav(`/products/customers/${r.id}`)} empty="No customers yet." />
    </ModuleWorkspace>
  );
}

const customerSchema = z.object({
  code: z.string().trim().min(1, 'Required').max(40), name: z.string().trim().min(1, 'Required').max(200), contactName: z.string().max(200).optional(),
  email: z.union([z.literal(''), z.string().email('Enter a valid email')]).optional(), phone: z.string().max(40).optional(), address: z.string().max(500).optional(),
  siteId: z.string().uuid('Choose a site'), departmentId: z.string().optional(), status: z.coerce.number().int(),
});

export function CustomerNew() {
  const nav = useNavigate();
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Customers', to: '/products/customers' }, { label: 'New' }]} title="New customer">
      <SchemaForm schema={customerSchema} submitLabel="Create customer" onCancel={() => nav('/products/customers')}
        defaults={{ code: '', name: '', contactName: '', email: '', phone: '', address: '', siteId: '', departmentId: '', status: 0 }}
        fields={[{ name: 'code', label: 'Customer code' }, { name: 'name', label: 'Name' }, { name: 'contactName', label: 'Contact' }, { name: 'email', label: 'Email' }, { name: 'phone', label: 'Phone' },
          { name: 'status', label: 'Status', type: 'select', options: statusOpts }, { name: 'siteId', label: 'Owning site', type: 'site' }, { name: 'departmentId', label: 'Department (optional)', type: 'department' },
          { name: 'address', label: 'Address', type: 'multiline' }]}
        onSubmit={async (v) => { const r = await post<{ id: string }>('/api/master/customers', { ...v, departmentId: v.departmentId || null, contactName: v.contactName || null, email: v.email || null, phone: v.phone || null, address: v.address || null }); nav(`/products/customers/${r.id}`); }} />
    </ModuleWorkspace>
  );
}

interface CustomerDto { id: string; code: string; name: string; contactName: string | null; email: string | null; phone: string | null; address: string | null; siteId: string; departmentId: string | null; status: number; rowVersion: number; canEdit: boolean }

export function CustomerDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const [tab, setTab] = useState('overview');
  const [dlg, setDlg] = useState(false);
  const q = useQuery({ queryKey: ['customer', id], queryFn: () => get<CustomerDto>(`/api/master/customers/${id}`) });
  const { error, run } = useRunner(() => qc.invalidateQueries({ queryKey: ['customer', id] }));
  const d = q.data;
  if (!d) return <ModuleWorkspace module={mod()} crumbs={[{ label: 'Customers', to: '/products/customers' }]} title="Loading">{q.error ? <Alert severity="error">{(q.error as Error).message}</Alert> : null}</ModuleWorkspace>;
  return (
    <ModuleWorkspace module={mod()} crumbs={[{ label: 'Customers', to: '/products/customers' }, { label: d.code }]}
      title={<Stack direction="row" gap={1.5} alignItems="center"><span style={{ fontFamily: mono }}>{d.code}</span><span>{d.name}</span><StatusChip status={masterStatus[d.status]} /></Stack>}
      tabs={[{ label: 'Overview', value: 'overview' }, { label: 'Files & history', value: 'more' }]} tab={tab} onTab={setTab}
      actions={d.canEdit ? <Button variant="outlined" onClick={() => setDlg(true)}>Edit</Button> : undefined}>
      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {tab === 'overview' && <Facts items={[['Contact', d.contactName ?? '—'], ['Email', d.email ?? '—'], ['Phone', d.phone ?? '—'], ['Address', d.address ?? '—']]} />}
      {tab === 'more' && <AttachmentsAndHistory type="Customer" id={d.id} canEdit={d.canEdit} />}
      <StepDialog open={dlg} title="Edit customer" confirm="Save" onClose={() => setDlg(false)}
        inputs={[{ name: 'name', label: 'Name', initial: d.name }, { name: 'contactName', label: 'Contact', initial: d.contactName ?? '' }, { name: 'email', label: 'Email', initial: d.email ?? '' },
          { name: 'phone', label: 'Phone', initial: d.phone ?? '' }, { name: 'address', label: 'Address', type: 'multiline', initial: d.address ?? '' }, { name: 'status', label: 'Status', type: 'select', initial: String(d.status), options: statusOpts }]}
        onConfirm={(v) => run(() => put(`/api/master/customers/${d.id}`, { code: d.code, name: v.name, contactName: v.contactName || null, email: v.email || null, phone: v.phone || null, address: v.address || null, siteId: d.siteId, departmentId: d.departmentId, status: Number(v.status), rowVersion: d.rowVersion }))} />
    </ModuleWorkspace>
  );
}
