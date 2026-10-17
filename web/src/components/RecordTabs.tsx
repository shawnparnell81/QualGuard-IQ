import { Stack } from '@mui/material';
import { ActionsPanel, ApprovalsPanel, CommentsPanel, FilesPanel, HistoryPanel, LinksPanel, TagsPanel } from './RecordPanels';

export { ActionsPanel, ApprovalsPanel, CommentsPanel, FilesPanel, HistoryPanel, LinksPanel, TagsPanel };

/** Tags, files, links, comments and history for any record, from the shared engines. */
export function AttachmentsAndHistory({ type, id, canEdit }: { type: string; id: string; canEdit: boolean }) {
  return (
    <Stack gap={3}>
      <TagsPanel type={type} id={id} canEdit={canEdit} />
      <FilesPanel type={type} id={id} canEdit={canEdit} />
      <LinksPanel type={type} id={id} canEdit={canEdit} />
      <CommentsPanel type={type} id={id} />
      <HistoryPanel type={type} id={id} />
    </Stack>
  );
}
