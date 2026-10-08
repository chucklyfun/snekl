interface WatercoolerModeFieldsProps {
  idPrefix: string;
  allowUpvotes: boolean;
  newestAtBottom: boolean;
  onAllowUpvotesChange: (value: boolean) => void;
  onNewestAtBottomChange: (value: boolean) => void;
}

export function WatercoolerModeFields({
  idPrefix,
  allowUpvotes,
  newestAtBottom,
  onAllowUpvotesChange,
  onNewestAtBottomChange,
}: WatercoolerModeFieldsProps) {
  return (
    <>
      <div className="form-row">
        <div className="form-group">
          <label htmlFor={`${idPrefix}-upvotes`}>Upvotes</label>
          <select
            id={`${idPrefix}-upvotes`}
            value={allowUpvotes ? 'on' : 'off'}
            onChange={(e) => onAllowUpvotesChange(e.target.value === 'on')}
          >
            <option value="off">Off</option>
            <option value="on">On</option>
          </select>
        </div>
        <div className="form-group">
          <label htmlFor={`${idPrefix}-order`}>Ordering</label>
          <select
            id={`${idPrefix}-order`}
            value={newestAtBottom ? 'top-to-bottom' : 'bottom-to-top'}
            onChange={(e) => onNewestAtBottomChange(e.target.value === 'top-to-bottom')}
          >
            <option value="top-to-bottom">Top to bottom</option>
            <option value="bottom-to-top">Bottom to top</option>
          </select>
        </div>
      </div>
      <p className="card-meta">
        Discord Style: Upvotes Off, Ordered Top to Bottom
        <br />
        Twitter/X Style: Upvotes On, Ordered Bottom to Top
      </p>
    </>
  );
}
