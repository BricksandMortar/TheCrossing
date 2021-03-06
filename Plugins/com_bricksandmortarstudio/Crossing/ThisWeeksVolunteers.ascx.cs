using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Entity;
using System.Linq;
using Quartz.Util;
using Rock;
using Rock.Attribute;
using Rock.Data;
using Rock.Model;
using Rock.Web.Cache;
using Rock.Web.UI.Controls;

namespace Plugins.com_bricksandmortarstudio.Crossing
{
    /// <summary>
    /// Template block for developers to use to start a new block.
    /// </summary>
    [DisplayName( "This Week's Volunteers" )]
    [Category( "Bricks and Mortar Studio" )]
    [Description( "Find the volunteers serving this week" )]

    [BooleanField( "Is External", "Is this block meant for external access", false )]
    public partial class ThisWeeksVolunteers : Rock.Web.UI.RockBlock
    {
        #region Fields

        // used for private variables

        #endregion

        #region Properties

        // used for public / protected properties

        #endregion

        #region Base Control Methods

        //  overrides of the base RockBlock methods (i.e. OnInit, OnLoad)

        /// <summary>
        /// Raises the <see cref="E:System.Web.UI.Control.Init" /> event.
        /// </summary>
        /// <param name="e">An <see cref="T:System.EventArgs" /> object that contains the event data.</param>
        protected override void OnInit( EventArgs e )
        {
            base.OnInit( e );
            gList.GridRebind += gList_GridRebind;

            // this event gets fired after block settings are updated. it's nice to repaint the screen if these settings would alter it
            this.BlockUpdated += Block_BlockUpdated;
            this.AddConfigurationUpdateTrigger( upnlContent );
        }

        /// <summary>
        /// Raises the <see cref="E:System.Web.UI.Control.Load" /> event.
        /// </summary>
        /// <param name="e">The <see cref="T:System.EventArgs" /> object that contains the event data.</param>
        protected override void OnLoad( EventArgs e )
        {
            base.OnLoad( e );
            hfGroupGuid.Value = PageParameter( "group" );
            var isExternal = GetAttributeValue( "IsExternal" ).AsBoolean();
            if ( isExternal && hfGroupGuid.Value.IsNullOrWhiteSpace() )
            {
                pnlView.Visible = false;
                return;
            }
            gFilter.Visible = isExternal;
            if ( !Page.IsPostBack )
            {
                SetFilter();
                BindGrid();
            }
        }

        #endregion

        #region Events

        // handlers called by the controls on your block

        /// <summary>
        /// Handles the BlockUpdated event of the control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        protected void Block_BlockUpdated( object sender, EventArgs e )
        {
            BindGrid();
        }

        /// <summary>
        /// Handles the GridRebind event of the gPledges control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        private void gList_GridRebind( object sender, EventArgs e )
        {
            BindGrid();
        }

        protected void gFilter_ApplyFilterClick( object sender, EventArgs e )
        {
            gFilter.SaveUserPreference( "Group", gpGroup.SelectedValue );
            BindGrid();
        }

        protected void gFilter_OnClearFilterClick( object sender, EventArgs e )
        {
            gFilter.SaveUserPreference( "Group", "" );
            gpGroup.SetValue( null );
            BindGrid();
        }

        protected void gFilter_OnDisplayFilterValue( object sender, GridFilter.DisplayFilterValueArgs e )
        {
            switch ( e.Key )
            {
                case "Group":
                    string groupName = string.Empty;

                    int? groupId = e.Value.AsIntegerOrNull();
                    if ( groupId.HasValue )
                    {
                        var groupService = new GroupService( new RockContext() );
                        var group = groupService.Get( groupId.Value );
                        if ( group != null )
                        {
                            groupName = group.Name;
                        }
                    }

                    e.Value = groupName;
                    break;
            }
        }

        #endregion

        #region Methods

        private void SetFilter()
        {
            var rockContext = new RockContext();
            int? groupId = gFilter.GetUserPreference( "Consolidator" ).AsIntegerOrNull();
            if ( groupId.HasValue )
            {
                var groupService = new GroupService( rockContext );
                var group = groupService.Get( groupId.Value );
                if ( group != null )
                {
                    gpGroup.SetValue( group );
                }
            }
        }

        /// <summary>
        /// Binds the grid.
        /// </summary>
        private void BindGrid()
        {
            var rockContext = new RockContext();
            // Get the team that should be serving this week
            string weekTeam = "Team " + com.bricksandmortarstudio.TheCrossing.Utils.ServingWeek.GetTeamNumber();

            // Get the group members who should be serving
            var attributeService = new AttributeService( rockContext );
            var groupMemberEntityType = EntityTypeCache.Read( Rock.SystemGuid.EntityType.GROUP_MEMBER.AsGuid() );
            var assignedTeamAttributeIds =
                attributeService.GetByEntityTypeId(
                    EntityTypeCache.Read( Rock.SystemGuid.EntityType.GROUP_MEMBER.AsGuid() ).Id ).Where( a => a.Key == "AssignedTeam" ).Select( a => a.Id );

            var assignedServicesAttributeIds =
                attributeService.GetByEntityTypeId( groupMemberEntityType.Id ).Where( a => a.Key == "AssignedServices" ).Select( a => a.Id );

            var attributeValueService = new AttributeValueService( rockContext );
            var groupMemberIds = new List<int>();

            foreach ( int attributeId in assignedTeamAttributeIds )
            {
                var attributeValues = attributeValueService.GetByAttributeId( attributeId ).AsQueryable().AsNoTracking().Where( av => av.Value.Contains( weekTeam ) );
                groupMemberIds.AddRange( attributeValues.Where( av => av.EntityId != null ).Select( av => av.EntityId.Value ) );
            }

            // Find the group member information to present
            IEnumerable<GroupMember> query = new GroupMemberService( rockContext ).GetListByIds( groupMemberIds );


            //Filtering to a specific group branch

            var groupService = new GroupService( rockContext );
            var group = hfGroupGuid.Value.AsGuidOrNull() != null
                ? groupService.GetByGuid( hfGroupGuid.Value.AsGuid() )
                : groupService.Get( gFilter.GetUserPreference( "Group" ).AsInteger() );

            if ( group != null )
            {
                var validGroupIds = new List<int> { group.Id };
                validGroupIds.AddRange( groupService.GetAllDescendents( group.Id ).Select( g => g.Id ) );

                query = query.Where( gm => validGroupIds.Contains( gm.GroupId ) );
            }


            var allScheduledPeople = query.Select( gm =>
                         new GroupAndPerson
                         {
                             Name = gm.Person.FullName,
                             GroupName = gm.Group.Name,
                             ParentGroup = gm.Group.ParentGroup,
                             ServiceTimes = attributeValueService.Queryable().AsNoTracking().Where( a => a.EntityId == gm.Id && assignedServicesAttributeIds.Contains( a.AttributeId ) ).FirstOrDefault()
                         } );

            // Sort and bind
            var sortProperty = gList.SortProperty;
            if ( sortProperty == null )
            {
                gList.DataSource = allScheduledPeople.OrderBy( gp => gp.GroupName ).ToList();
            }
            else
            {
                gList.DataSource = allScheduledPeople.AsQueryable().Sort( sortProperty ).ToList();
            }
            gList.DataBind();
        }

        #endregion


    }

    internal class GroupAndPerson
    {
        public string Name { get; set; }

        public string GroupName { get; set; }

        public string WeekToServe { get; set; }

        public Group ParentGroup { get; set; }

        public AttributeValue ServiceTimes { get; set; }
    }
}